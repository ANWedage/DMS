using DMS.Data;
using DMS.Models;
using DMS.Services;
using MongoDB.Driver;

namespace DMS.Api;

public sealed class EmailOutboxService : IEmailOutbox
{
    private readonly MongoDbContext _context;

    public EmailOutboxService(MongoDbContext context)
    {
        _context = context;
    }

    public void EnqueueUserAlert(string eventKey, string recipientUserId, string recipientEmail,
        string recipientName, string senderName, string alertType)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
            return;

        var item = new EmailOutboxItem
        {
            DeduplicationKey = eventKey,
            RecipientUserId = recipientUserId,
            RecipientEmail = recipientEmail,
            RecipientName = recipientName,
            SenderName = senderName,
            AlertType = alertType,
            NextAttemptAt = DateTime.UtcNow
        };

        _context.EmailOutbox.UpdateOne(
            item => item.DeduplicationKey == eventKey,
            Builders<EmailOutboxItem>.Update
                .SetOnInsert(item => item.Id, item.Id)
                .SetOnInsert(item => item.DeduplicationKey, item.DeduplicationKey)
                .SetOnInsert(item => item.RecipientUserId, item.RecipientUserId)
                .SetOnInsert(item => item.RecipientEmail, item.RecipientEmail)
                .SetOnInsert(item => item.RecipientName, item.RecipientName)
                .SetOnInsert(item => item.SenderName, item.SenderName)
                .SetOnInsert(item => item.AlertType, item.AlertType)
                .SetOnInsert(item => item.Status, item.Status)
                .SetOnInsert(item => item.Attempts, item.Attempts)
                .SetOnInsert(item => item.NextAttemptAt, item.NextAttemptAt)
                .SetOnInsert(item => item.CreatedAt, item.CreatedAt),
            new UpdateOptions { IsUpsert = true });
    }
}
