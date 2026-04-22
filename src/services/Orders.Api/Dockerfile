FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["Ecommerce.TestPlatform.slnx", "."]
COPY ["src/building-blocks/SharedKernel/SharedKernel.csproj", "src/building-blocks/SharedKernel/"]
COPY ["src/services/Orders.Api/Orders.Api.csproj", "src/services/Orders.Api/"]

RUN dotnet restore "src/services/Orders.Api/Orders.Api.csproj"

COPY ["src/building-blocks/SharedKernel/", "src/building-blocks/SharedKernel/"]
COPY ["src/services/Orders.Api/", "src/services/Orders.Api/"]

RUN dotnet publish "src/services/Orders.Api/Orders.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Orders.Api.dll"]
