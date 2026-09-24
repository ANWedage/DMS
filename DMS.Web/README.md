# DMS mobile web app

The mobile web app is a separate ASP.NET Core Razor Pages client for daily attendance and work updates. It uses the existing DMS API and does not connect directly to MongoDB.

## Configuration

Set `DMS_API_BASE_URL` to the deployed API URL, for example:

```text
DMS_API_BASE_URL=https://dms-api.example.onrender.com
```

For local development, the fallback value is configured in `appsettings.json`.

## Render

The `render.yaml` file defines the `dms-mobile-web` service. Configure its `DMS_API_BASE_URL` environment variable to the deployed `dms-api` URL. The service health check is `/health`.

The web app uses secure, HTTP-only cookies. Developer accounts use the developer daily-update pages, while admin accounts use the admin daily-work page. Submission authorization, role enforcement, and one-update-per-day enforcement remain in the API.
