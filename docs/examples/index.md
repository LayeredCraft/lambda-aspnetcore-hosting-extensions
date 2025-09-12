# Examples

Real-world usage examples and patterns for LayeredCraft Lambda ASP.NET Core Hosting Extensions.

## Basic API with Timeout Handling

A simple REST API that handles Lambda timeouts gracefully.

### Startup Configuration

```csharp
using LayeredCraft.Lambda.AspNetCore.Hosting.Extensions;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
        services.AddHttpClient<IExternalApiService, ExternalApiService>();
        services.AddScoped<IDataRepository, DataRepository>();
        
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddAWSProvider();
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        // Add Lambda timeout middleware early in pipeline
        app.UseLambdaTimeoutLinkedCancellation();
        
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}
```

### Controller Implementation

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IDataRepository _repository;
    private readonly IExternalApiService _externalApi;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IDataRepository repository,
        IExternalApiService externalApi,
        ILogger<UsersController> logger)
    {
        _repository = repository;
        _externalApi = externalApi;
        _logger = logger;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(string id, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Fetching user {UserId}", id);
            
            // Repository call respects cancellation
            var user = await _repository.GetUserAsync(id, cancellationToken);
            if (user == null)
                return NotFound();

            // Enrich with external data if time permits
            try
            {
                user.ExternalData = await _externalApi.GetUserEnrichmentAsync(id, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("External enrichment cancelled for user {UserId}", id);
                // Continue without external data
            }

            return Ok(user);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("User fetch cancelled due to timeout for {UserId}", id);
            return StatusCode(504, "Request timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching user {UserId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, 
                                              CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Creating user {Email}", request.Email);
            
            // Validate request
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Check for existing user
            var existing = await _repository.GetUserByEmailAsync(request.Email, cancellationToken);
            if (existing != null)
                return Conflict("User already exists");

            // Create user with timeout awareness
            var user = await _repository.CreateUserAsync(request, cancellationToken);
            
            // Send welcome email (fire-and-forget with timeout)
            _ = Task.Run(async () =>
            {
                using var emailCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _externalApi.SendWelcomeEmailAsync(user.Email, emailCts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send welcome email to {Email}", user.Email);
                }
            }, CancellationToken.None);

            return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("User creation cancelled due to timeout for {Email}", request.Email);
            return StatusCode(504, "Request timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user {Email}", request.Email);
            return StatusCode(500, "Internal server error");
        }
    }
}
```

## Data Processing API with Batch Operations

An API that processes large datasets with timeout awareness.

### Service Implementation

```csharp
public interface IDataProcessingService
{
    Task<ProcessingResult> ProcessDataAsync(ProcessingRequest request, CancellationToken cancellationToken);
    Task<BatchResult> ProcessBatchAsync(IEnumerable<DataItem> items, CancellationToken cancellationToken);
}

public class DataProcessingService : IDataProcessingService
{
    private readonly ILogger<DataProcessingService> _logger;
    private readonly IExternalService _externalService;

    public DataProcessingService(ILogger<DataProcessingService> logger, IExternalService externalService)
    {
        _logger = logger;
        _externalService = externalService;
    }

    public async Task<ProcessingResult> ProcessDataAsync(ProcessingRequest request, 
                                                       CancellationToken cancellationToken)
    {
        var result = new ProcessingResult { StartTime = DateTime.UtcNow };
        
        try
        {
            _logger.LogInformation("Starting data processing for {RequestId}", request.Id);
            
            // Step 1: Validate data (quick operation)
            var validationResult = await ValidateDataAsync(request.Data, cancellationToken);
            if (!validationResult.IsValid)
            {
                result.Errors.AddRange(validationResult.Errors);
                return result;
            }

            // Step 2: Process in batches with timeout checks
            var batches = request.Data.Chunk(100); // Process 100 items at a time
            var processedCount = 0;
            
            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var batchResult = await ProcessBatchAsync(batch, cancellationToken);
                result.ProcessedItems += batchResult.ProcessedCount;
                result.Errors.AddRange(batchResult.Errors);
                
                processedCount += batch.Length;
                _logger.LogDebug("Processed {Count}/{Total} items", 
                               processedCount, request.Data.Length);
            }

            // Step 3: Finalize processing
            await FinalizeProcessingAsync(result, cancellationToken);
            
            result.IsSuccess = result.Errors.Count == 0;
            result.EndTime = DateTime.UtcNow;
            
            _logger.LogInformation("Completed data processing for {RequestId}. " +
                                 "Processed: {Processed}, Errors: {Errors}, Duration: {Duration}ms",
                                 request.Id, result.ProcessedItems, result.Errors.Count,
                                 (result.EndTime - result.StartTime).TotalMilliseconds);
            
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.EndTime = DateTime.UtcNow;
            result.IsCancelled = true;
            
            _logger.LogWarning("Data processing cancelled for {RequestId} after {Duration}ms. " +
                             "Processed: {Processed} items",
                             request.Id, 
                             (result.EndTime - result.StartTime).TotalMilliseconds,
                             result.ProcessedItems);
            throw;
        }
        catch (Exception ex)
        {
            result.EndTime = DateTime.UtcNow;
            result.IsSuccess = false;
            result.Errors.Add($"Processing failed: {ex.Message}");
            
            _logger.LogError(ex, "Data processing failed for {RequestId}", request.Id);
            throw;
        }
    }

    public async Task<BatchResult> ProcessBatchAsync(IEnumerable<DataItem> items, 
                                                   CancellationToken cancellationToken)
    {
        var result = new BatchResult();
        var tasks = new List<Task<ItemResult>>();
        
        // Process items concurrently with timeout awareness
        foreach (var item in items)
        {
            tasks.Add(ProcessSingleItemAsync(item, cancellationToken));
            
            // Limit concurrent operations to prevent resource exhaustion
            if (tasks.Count >= 10)
            {
                var completed = await Task.WhenAny(tasks);
                var itemResult = await completed;
                
                tasks.Remove(completed);
                UpdateBatchResult(result, itemResult);
            }
        }
        
        // Process remaining items
        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks);
            var itemResult = await completed;
            
            tasks.Remove(completed);
            UpdateBatchResult(result, itemResult);
        }
        
        return result;
    }

    private async Task<ItemResult> ProcessSingleItemAsync(DataItem item, CancellationToken cancellationToken)
    {
        try
        {
            // Simulate processing with external API call
            var processedData = await _externalService.ProcessItemAsync(item, cancellationToken);
            
            return new ItemResult
            {
                Id = item.Id,
                IsSuccess = true,
                ProcessedData = processedData
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Propagate cancellation
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process item {ItemId}", item.Id);
            
            return new ItemResult
            {
                Id = item.Id,
                IsSuccess = false,
                Error = ex.Message
            };
        }
    }
}
```

### Controller with Timeout Handling

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProcessingController : ControllerBase
{
    private readonly IDataProcessingService _processingService;
    private readonly ILogger<ProcessingController> _logger;

    public ProcessingController(IDataProcessingService processingService, 
                              ILogger<ProcessingController> logger)
    {
        _processingService = processingService;
        _logger = logger;
    }

    [HttpPost("process")]
    public async Task<IActionResult> ProcessData([FromBody] ProcessingRequest request, 
                                               CancellationToken cancellationToken)
    {
        if (request?.Data == null || !request.Data.Any())
        {
            return BadRequest("No data provided for processing");
        }

        try
        {
            var result = await _processingService.ProcessDataAsync(request, cancellationToken);
            
            if (result.IsSuccess)
            {
                return Ok(new
                {
                    message = "Processing completed successfully",
                    processedItems = result.ProcessedItems,
                    duration = (result.EndTime - result.StartTime).TotalMilliseconds
                });
            }
            else
            {
                return BadRequest(new
                {
                    message = "Processing completed with errors",
                    processedItems = result.ProcessedItems,
                    errors = result.Errors,
                    duration = (result.EndTime - result.StartTime).TotalMilliseconds
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Processing request cancelled for {RequestId}", request.Id);
            return StatusCode(504, new
            {
                message = "Processing was cancelled due to timeout",
                requestId = request.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing request {RequestId}", request.Id);
            return StatusCode(500, "An unexpected error occurred");
        }
    }
}
```

## File Upload with Progress Tracking

Handling file uploads with timeout awareness and progress tracking.

### File Upload Controller

```csharp
[ApiController]
[Route("api/[controller]")]
public class FileController : ControllerBase
{
    private readonly IFileProcessingService _fileService;
    private readonly ILogger<FileController> _logger;

    public FileController(IFileProcessingService fileService, ILogger<FileController> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file provided");
        }

        var uploadId = Guid.NewGuid().ToString();
        
        try
        {
            _logger.LogInformation("Starting file upload {UploadId}. File: {FileName}, Size: {Size} bytes",
                                 uploadId, file.FileName, file.Length);

            using var stream = file.OpenReadStream();
            var result = await _fileService.ProcessFileAsync(
                uploadId, 
                file.FileName, 
                stream, 
                cancellationToken);

            return Ok(new
            {
                uploadId = uploadId,
                fileName = file.FileName,
                size = file.Length,
                processedRecords = result.ProcessedRecords,
                duration = result.ProcessingTime.TotalMilliseconds
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("File upload cancelled {UploadId}", uploadId);
            return StatusCode(504, new
            {
                message = "File upload was cancelled due to timeout",
                uploadId = uploadId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File upload failed {UploadId}", uploadId);
            return StatusCode(500, "File upload failed");
        }
    }
}
```

### File Processing Service

```csharp
public class FileProcessingService : IFileProcessingService
{
    private readonly ILogger<FileProcessingService> _logger;

    public FileProcessingService(ILogger<FileProcessingService> logger)
    {
        _logger = logger;
    }

    public async Task<FileProcessingResult> ProcessFileAsync(string uploadId, 
                                                           string fileName, 
                                                           Stream fileStream, 
                                                           CancellationToken cancellationToken)
    {
        var result = new FileProcessingResult { StartTime = DateTime.UtcNow };
        
        try
        {
            using var reader = new StreamReader(fileStream);
            var recordCount = 0;
            var processedCount = 0;
            
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                recordCount++;
                
                // Process each line/record
                if (await ProcessRecordAsync(line, cancellationToken))
                {
                    processedCount++;
                }
                
                // Log progress periodically
                if (recordCount % 1000 == 0)
                {
                    _logger.LogDebug("Processing file {UploadId}: {Processed}/{Total} records",
                                   uploadId, processedCount, recordCount);
                }
            }
            
            result.ProcessedRecords = processedCount;
            result.TotalRecords = recordCount;
            result.EndTime = DateTime.UtcNow;
            result.ProcessingTime = result.EndTime - result.StartTime;
            
            _logger.LogInformation("Completed file processing {UploadId}. " +
                                 "Processed: {Processed}/{Total} records in {Duration}ms",
                                 uploadId, processedCount, recordCount, 
                                 result.ProcessingTime.TotalMilliseconds);
            
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.EndTime = DateTime.UtcNow;
            result.ProcessingTime = result.EndTime - result.StartTime;
            
            _logger.LogWarning("File processing cancelled {UploadId} after {Duration}ms. " +
                             "Processed: {Processed} records",
                             uploadId, result.ProcessingTime.TotalMilliseconds, result.ProcessedRecords);
            throw;
        }
    }

    private async Task<bool> ProcessRecordAsync(string record, CancellationToken cancellationToken)
    {
        try
        {
            // Simulate record processing
            await Task.Delay(1, cancellationToken); // Minimal delay for cancellation checks
            
            // Actual processing logic here
            return !string.IsNullOrWhiteSpace(record);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process record: {Record}", record);
            return false;
        }
    }
}
```

## Custom Safety Buffer Configuration

Different scenarios requiring different safety buffer configurations.

### Environment-Based Configuration

```csharp
public class Startup
{
    private readonly IConfiguration _configuration;

    public Startup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // Configure safety buffer based on environment
        var safetyBufferMs = _configuration.GetValue<int>("Lambda:SafetyBufferMs", 250);
        var safetyBuffer = TimeSpan.FromMilliseconds(safetyBufferMs);
        
        app.UseLambdaTimeoutLinkedCancellation(safetyBuffer);
        
        // Other middleware...
    }
}
```

### Operation-Specific Buffers

```csharp
public class CustomTimeoutController : ControllerBase
{
    [HttpPost("quick-operation")]
    public async Task<IActionResult> QuickOperation(CancellationToken cancellationToken)
    {
        // For quick operations, use a shorter timeout to maximize processing time
        using var shortCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, shortCts.Token);
        
        try
        {
            var result = await ProcessQuicklyAsync(linkedCts.Token);
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, "Operation timed out");
        }
    }

    [HttpPost("heavy-operation")]
    public async Task<IActionResult> HeavyOperation(CancellationToken cancellationToken)
    {
        // For heavy operations, respect the middleware's timeout
        try
        {
            var result = await ProcessHeavilyAsync(cancellationToken);
            return Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, "Operation timed out");
        }
    }
}
```

## Testing with Timeout Scenarios

Unit tests for timeout handling.

### Test Setup

```csharp
[TestFixture]
public class TimeoutHandlingTests
{
    private TestServer _server;
    private HttpClient _client;

    [SetUp]
    public void Setup()
    {
        var builder = new WebHostBuilder()
            .UseStartup<TestStartup>()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IExternalService, MockExternalService>();
            });

        _server = new TestServer(builder);
        _client = _server.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _server?.Dispose();
    }

    [Test]
    public async Task Should_Handle_Timeout_Gracefully()
    {
        // Arrange
        var request = new ProcessingRequest
        {
            Id = "test-123",
            Data = Enumerable.Range(1, 1000).Select(i => new DataItem { Id = i.ToString() }).ToArray()
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        var response = await _client.PostAsJsonAsync("/api/processing/process", request, cts.Token);
        
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.GatewayTimeout));
    }

    [Test]
    public async Task Should_Complete_Quick_Operations()
    {
        // Arrange
        var request = new ProcessingRequest
        {
            Id = "test-456",
            Data = new[] { new DataItem { Id = "1" } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/processing/process", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
```

These examples demonstrate comprehensive patterns for using the Lambda timeout middleware in real-world scenarios, showing how to handle various timeout situations gracefully while maintaining good observability and user experience.