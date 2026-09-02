FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DMS.Api/DMS.Api.csproj DMS.Api/
COPY DMS.Core/DMS.Core.csproj DMS.Core/
RUN dotnet restore DMS.Api/DMS.Api.csproj

COPY DMS.Api/ DMS.Api/
COPY DMS.Core/ DMS.Core/
COPY Data/ Data/
COPY Helpers/ Helpers/
COPY Models/ Models/
COPY Services/ Services/
RUN dotnet publish DMS.Api/DMS.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_hostBuilder__reloadConfigOnChange=false
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DMS.Api.dll"]