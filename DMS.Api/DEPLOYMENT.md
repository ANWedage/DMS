# DMS API deployment

Build the container from the repository root:

```powershell
docker build -f .\DMS.Api\Dockerfile -t dms-api .
```

Run it locally without putting secrets in the image:

```powershell
docker run --rm -p 8080:8080 `
  -e DMS_MONGO_CONNECTION_STRING="<MongoDB connection string>" `
  -e DMS_JWT_SECRET="<random value with at least 32 characters>" `
  -e DMS_EMAIL_SMTP_HOST="<smtp host>" `
  -e DMS_EMAIL_SMTP_PORT="587" `
  -e DMS_EMAIL_SMTP_USERNAME="<smtp username>" `
  -e DMS_EMAIL_SMTP_PASSWORD="<smtp password>" `
  -e DMS_EMAIL_SENDER_ADDRESS="<sender email>" `
  -e DMS_EMAIL_SENDER_NAME="DMS" `
  -e DMS_EMAIL_APPLICATION_URL="<dms url>" `
  dms-api
```

Configure the email environment variables in the hosting provider's secret settings. Do not place credentials in the Dockerfile, Git repository, or desktop release ZIP.

The container disables ASP.NET configuration file watching because hosted containers receive configuration through environment variables and do not need live file reloads. This also avoids file-watcher limits on low-cost Linux hosts.

After deployment, confirm `https://<api-host>/health` returns `{"status":"ok"}`. Configure the desktop application with:

```text
DMS_API_BASE_URL=https://<api-host>
```

The API must use HTTPS in hosted environments. The MongoDB Atlas network access rules must allow the hosting provider's outbound connections, and the database user should have only the permissions required by the DMS database.