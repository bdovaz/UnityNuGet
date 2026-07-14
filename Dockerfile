FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664 AS build

ARG TARGETARCH

WORKDIR /app

RUN mkdir -p src/UnityNuGet && \
    mkdir -p src/UnityNuGet.Server && \
    mkdir -p src/UnityNuGet.Server.Tests && \
    mkdir -p src/UnityNuGet.Tests && \
    mkdir -p src/UnityNuGet.Tool

COPY src/Directory.Build.props src/Directory.Build.props
COPY src/Directory.Packages.props src/Directory.Packages.props
COPY src/*.slnx src
COPY src/UnityNuGet/*.csproj src/UnityNuGet
COPY src/UnityNuGet.Server/*.csproj src/UnityNuGet.Server
COPY src/UnityNuGet.Server.Tests/*.csproj src/UnityNuGet.Server.Tests
COPY src/UnityNuGet.Tests/*.csproj src/UnityNuGet.Tests
COPY src/UnityNuGet.Tool/*.csproj src/UnityNuGet.Tool

RUN dotnet restore src -a "$TARGETARCH"

COPY . ./

RUN dotnet publish src/UnityNuGet.Server -a "$TARGETARCH" -c Release -o /app/src/out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1fa23fc4872d95fd71c2833ebe65d7e84a43b2d51a31d119516852f13d9505a7

ARG TARGETPLATFORM

RUN apt-get update && \
    apt-get install -y curl unzip && \
    rm -rf /var/lib/apt/lists/* && \
    # linux/arm64 images do not support the upm-cli tool, so we only install it on other platforms
    # https://discussions.unity.com/t/package-manager-changes-package-signing-and-status-labels/1688660/72
    if [ "$TARGETPLATFORM" = "linux/amd64" ]; then \
      curl -fsSL https://cdn.packages.unity.com/upm-cli/install.sh | bash; \
      echo "upm cli version: $($HOME/.upm/bin/upm --version)"; \
    fi

WORKDIR /app

COPY --from=build /app/src/out .

HEALTHCHECK CMD curl --fail http://localhost:${ASPNETCORE_HTTP_PORTS}/health || exit 1

ENTRYPOINT ["dotnet", "UnityNuGet.Server.dll"]
