namespace DMS.Services;

public interface IEmailOutbox
{
    void EnqueueUserAlert(string eventKey, string recipientUserId, string recipientEmail,
        string recipientName, string senderName, string alertType);
}
