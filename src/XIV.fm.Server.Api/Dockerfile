FROM mcr.microsoft.com/dotnet/sdk:10.0.301@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630730f2c8201ed0572b295d5 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props XIV.fm.slnx ./
COPY src/XIV.fm.Contracts/XIV.fm.Contracts.csproj src/XIV.fm.Contracts/packages.lock.json src/XIV.fm.Contracts/
COPY src/XIV.fm.Server.Domain/XIV.fm.Server.Domain.csproj src/XIV.fm.Server.Domain/packages.lock.json src/XIV.fm.Server.Domain/
COPY src/XIV.fm.Server.Application/XIV.fm.Server.Application.csproj src/XIV.fm.Server.Application/packages.lock.json src/XIV.fm.Server.Application/
COPY src/XIV.fm.Server.Infrastructure/XIV.fm.Server.Infrastructure.csproj src/XIV.fm.Server.Infrastructure/packages.lock.json src/XIV.fm.Server.Infrastructure/
COPY src/XIV.fm.Server.Api/XIV.fm.Server.Api.csproj src/XIV.fm.Server.Api/packages.lock.json src/XIV.fm.Server.Api/
RUN dotnet restore src/XIV.fm.Server.Api/XIV.fm.Server.Api.csproj --locked-mode

COPY src/XIV.fm.Contracts/ src/XIV.fm.Contracts/
COPY src/XIV.fm.Server.Domain/ src/XIV.fm.Server.Domain/
COPY src/XIV.fm.Server.Application/ src/XIV.fm.Server.Application/
COPY src/XIV.fm.Server.Infrastructure/ src/XIV.fm.Server.Infrastructure/
COPY src/XIV.fm.Server.Api/ src/XIV.fm.Server.Api/
RUN dotnet publish src/XIV.fm.Server.Api/XIV.fm.Server.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9@sha256:7644f992230d35cf230017189d4038c0ae0f7388b13f4f7ae1900a155bafb597 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
COPY --from=build --chown=app:app /app/publish/ ./
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "XIV.fm.Server.Api.dll"]
