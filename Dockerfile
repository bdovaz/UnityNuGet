FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:adc02be8b87957d07208a4a3e51775935b33bad3317de8c45b1e67357b4c073b AS build
ARG TARGETARCH
WORKDIR /app

RUN mkdir -p src/UnityNuGet && \
    mkdir -p src/UnityNuGet.Server && \
    mkdir -p src/UnityNuGet.Server.Tests && \
    mkdir -p src/UnityNuGet.Tests && \
    mkdir -p src/UnityNuGet.Tool

COPY src/Directory.Build.props src/Directory.Build.props
COPY src/Directory.Packages.props src/Directory.Packages.props
COPY src/*.sln src
COPY src/UnityNuGet/*.csproj src/UnityNuGet
COPY src/UnityNuGet.Server/*.csproj src/UnityNuGet.Server
COPY src/UnityNuGet.Server.Tests/*.csproj src/UnityNuGet.Server.Tests
COPY src/UnityNuGet.Tests/*.csproj src/UnityNuGet.Tests
COPY src/UnityNuGet.Tool/*.csproj src/UnityNuGet.Tool
RUN dotnet restore src -a "$TARGETARCH"

COPY . ./
RUN dotnet publish src/UnityNuGet.Server -a "$TARGETARCH" -c Release -o /app/src/out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:9271888dde1f4408dfb7ce75bc0f513c903d20ff2f1287ab153b641d4588ec7d
RUN apt-get update && \
    apt-get install -y curl && \
    rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/src/out .
ENTRYPOINT ["dotnet", "UnityNuGet.Server.dll"]
