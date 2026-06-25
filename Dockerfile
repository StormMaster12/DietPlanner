FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DietPlanner/DietPlanner.csproj DietPlanner/
RUN dotnet restore DietPlanner/DietPlanner.csproj

COPY DietPlanner/ DietPlanner/
RUN dotnet publish DietPlanner/DietPlanner.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DietPlanner.dll"]
