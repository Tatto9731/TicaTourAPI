# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["TicaTourAPI/TicaTourAPI.csproj", "TicaTourAPI/"]
RUN dotnet restore "TicaTourAPI/TicaTourAPI.csproj"

COPY . .
WORKDIR "/src/TicaTourAPI"
RUN dotnet publish "TicaTourAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000}
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 10000

ENTRYPOINT ["dotnet", "TicaTourAPI.dll"]