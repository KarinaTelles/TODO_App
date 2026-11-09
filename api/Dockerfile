# Build context: repository root (see compose.yaml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY api/TaskAssistant.Api/TaskAssistant.Api.csproj api/TaskAssistant.Api/
RUN dotnet restore api/TaskAssistant.Api/TaskAssistant.Api.csproj
COPY api/TaskAssistant.Api/ api/TaskAssistant.Api/
RUN dotnet publish api/TaskAssistant.Api/TaskAssistant.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The SQLite file lives in /data so a volume can keep it between restarts.
USER root
RUN mkdir -p /data && chown app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Tasks="Data Source=/data/tasks.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "TaskAssistant.Api.dll"]
