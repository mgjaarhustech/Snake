FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
# copy published files during CI; or build here if you prefer multi-stage
COPY ./bin/Release/net8.0/linux-x64/publish/ ./
EXPOSE 8080 50051
ENTRYPOINT ["./Snake.EnvServer","--cols","40","--rows","30","--timeout-mult","150","--rest-port","8080","--grpc-port","50051"]