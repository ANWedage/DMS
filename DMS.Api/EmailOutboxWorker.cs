using System.Net;
using System.Net.Mail;
using DMS.Data;
using DMS.Models;
using MongoDB.Driver;

namespace DMS.Api;

public sealed class EmailOutboxWorker : BackgroundService
{
    private const string Pending = "Pending";
    private const string Sending = "Sending";
    private const string Sent = "Sent";
    private const string Failed = "Failed";
    private readonly MongoDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailOutboxWorker> _logger;

    public EmailOutboxWorker(MongoDbContext context, IConfiguration configuration, ILogger<EmailOutboxWorker> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email outbox processing failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task ProcessOneAsync(CancellationToken cancellationToken)
    {
        var smtpHost = GetSetting("Email:SmtpHost", "DMS_EMAIL_SMTP_HOST");
        var senderAddress = GetSetting("Email:SenderAddress", "DMS_EMAIL_SENDER_ADDRESS");
        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(senderAddress))
            return;

        var now = DateTime.UtcNow;
        var retryableStatus = Builders<EmailOutboxItem>.Filter.In(item => item.Status, new[] { Pending, Failed });
        var abandonedProcessing = Builders<EmailOutboxItem>.Filter.And(
            Builders<EmailOutboxItem>.Filter.Eq(item => item.Status, Sending),
            Builders<EmailOutboxItem>.Filter.Lt(item => item.ProcessingStartedAt, now.AddMinutes(-10)));
        var item = _context.EmailOutbox.FindOneAndUpdate(
            Builders<EmailOutboxItem>.Filter.And(
                Builders<EmailOutboxItem>.Filter.Or(retryableStatus, abandonedProcessing),
                Builders<EmailOutboxItem>.Filter.Lte(item => item.NextAttemptAt, now),
                Builders<EmailOutboxItem>.Filter.Lt(item => item.Attempts, 5)),
                Builders<EmailOutboxItem>.Update
                    .Set(item => item.Status, Sending)
                .Set(item => item.ProcessingStartedAt, now)
                    .Inc(item => item.Attempts, 1),
            new FindOneAndUpdateOptions<EmailOutboxItem>
            {
                Sort = Builders<EmailOutboxItem>.Sort.Ascending(item => item.CreatedAt),
                ReturnDocument = ReturnDocument.After
            });

        if (item == null)
            return;

        try
        {
            await SendEmailAsync(item, smtpHost, senderAddress, cancellationToken);
            _context.EmailOutbox.UpdateOne(
                stored => stored.Id == item.Id,
                Builders<EmailOutboxItem>.Update
                    .Set(stored => stored.Status, Sent)
                    .Set(stored => stored.SentAt, DateTime.UtcNow)
                    .Unset(stored => stored.ProcessingStartedAt)
                    .Unset(stored => stored.LastError));
        }
        catch (Exception ex)
        {
            _context.EmailOutbox.UpdateOne(
                stored => stored.Id == item.Id,
                Builders<EmailOutboxItem>.Update
                    .Set(stored => stored.Status, Failed)
                    .Set(stored => stored.LastError, ex.Message)
                    .Set(stored => stored.NextAttemptAt, DateTime.UtcNow.AddMinutes(Math.Min(item.Attempts * 5, 60)))
                    .Unset(stored => stored.ProcessingStartedAt));
            _logger.LogWarning(ex, "Email delivery failed for outbox item {OutboxId}.", item.Id);
        }
    }

    private async Task SendEmailAsync(EmailOutboxItem item, string smtpHost, string senderAddress, CancellationToken cancellationToken)
    {
        var port = int.TryParse(GetSetting("Email:SmtpPort", "DMS_EMAIL_SMTP_PORT"), out var configuredPort)
            ? configuredPort
            : 587;
        var username = GetSetting("Email:SmtpUsername", "DMS_EMAIL_SMTP_USERNAME");
        var password = GetSetting("Email:SmtpPassword", "DMS_EMAIL_SMTP_PASSWORD");
        var senderName = GetSetting("Email:SenderName", "DMS_EMAIL_SENDER_NAME") ?? "DMS";
        var applicationUrl = GetSetting("Email:ApplicationUrl", "DMS_EMAIL_APPLICATION_URL");
        var alertLabel = string.Equals(item.AlertType, "Message", StringComparison.OrdinalIgnoreCase)
            ? "new message"
            : "new notification";

        using var mail = new MailMessage
        {
            From = new MailAddress(senderAddress, senderName),
            Subject = $"You have a new {alertLabel} in DMS",
            Body = BuildBody(item, alertLabel, applicationUrl),
            IsBodyHtml = false
        };
        mail.To.Add(new MailAddress(item.RecipientEmail, item.RecipientName));

        using var client = new SmtpClient(smtpHost, port)
        {
            EnableSsl = true,
            Credentials = string.IsNullOrWhiteSpace(username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(username, password)
        };
        await client.SendMailAsync(mail, cancellationToken);
    }

    private static string BuildBody(EmailOutboxItem item, string alertLabel, string? applicationUrl)
    {
        var body = $"Hello {item.RecipientName},\n\nYou have received a {alertLabel} from {item.SenderName}.\n\nPlease open DMS to view it.";
        return string.IsNullOrWhiteSpace(applicationUrl)
            ? body
            : $"{body}\n\nOpen DMS: {applicationUrl}";
    }

    private string? GetSetting(string configurationKey, string environmentKey)
    {
        var configuredValue = _configuration[configurationKey];
        return !string.IsNullOrWhiteSpace(configuredValue)
            ? configuredValue
            : Environment.GetEnvironmentVariable(environmentKey);
    }
}
