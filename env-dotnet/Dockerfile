# build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish env-dotnet/Snake.EnvServer.csproj -c Release -r linux-x64 -o /app/publish -p:PublishSingleFile=true

# runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish/ ./
EXPOSE 8080 50051
ENTRYPOINT ["./Snake.EnvServer","--cols","30","--rows","30","--timeout-mult","150","--rest-port","8080","--grpc-port","50051"]
