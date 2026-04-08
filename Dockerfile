FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["WebProxyAuditor.API.csproj", "./"]
RUN dotnet restore "./WebProxyAuditor.API.csproj"

COPY . .
RUN dotnet publish "./WebProxyAuditor.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    chromium \
    ca-certificates \
    fonts-liberation \
    libasound2 \
    libatk-bridge2.0-0 \
    libatk1.0-0 \
    libc6 \
    libcairo2 \
    libcups2 \
    libdbus-1-3 \
    libdrm2 \
    libexpat1 \
    libfontconfig1 \
    libgbm1 \
    libglib2.0-0 \
    libgtk-3-0 \
    libnspr4 \
    libnss3 \
    libpango-1.0-0 \
    libpangocairo-1.0-0 \
    libstdc++6 \
    libx11-6 \
    libx11-xcb1 \
    libxcb1 \
    libxcomposite1 \
    libxdamage1 \
    libxext6 \
    libxfixes3 \
    libxrandr2 \
    wget \
    xdg-utils \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=8080
ENV BROWSER_EXECUTABLE_PATH=/usr/bin/chromium

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "WebProxyAuditor.API.dll"]
