using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using CorePoints.OutboxWorker.Interfaces;
using CorePoints.OutboxWorker.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace CorePoints.OutboxWorker.Publishers;

public sealed class SnsEventPublisher : IEventPublisher
{
    private readonly IAmazonSimpleNotificationService _snsClient;
    private readonly OutboxWorkerOptions _options;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<SnsEventPublisher> _logger;

    public SnsEventPublisher(
        IAmazonSimpleNotificationService snsClient,
        IOptions<OutboxWorkerOptions> options,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<SnsEventPublisher> logger)
    {
        _snsClient = snsClient;
        _options = options.Value;
        _resiliencePipeline = pipelineProvider.GetPipeline("sns-publish");
        _logger = logger;
    }

    public async Task<PublishResult> PublishAsync(
        OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var request = new PublishRequest
                {
                    TopicArn = _options.SnsTopicArn,
                    Message = outboxEvent.Payload,
                    MessageDeduplicationId = outboxEvent.Id.ToString(),
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        ["EventType"] = new MessageAttributeValue
                        {
                            DataType = "String",
                            StringValue = outboxEvent.EventType
                        },
                        ["CorrelationId"] = new MessageAttributeValue
                        {
                            DataType = "String",
                            StringValue = outboxEvent.CorrelationId
                        }
                    }
                };

                return await _snsClient.PublishAsync(request, ct);
            }, cancellationToken);

            return new PublishResult
            {
                Success = true,
                MessageId = result.MessageId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish event {EventId} of type {EventType}",
                outboxEvent.Id, outboxEvent.EventType);

            return new PublishResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
