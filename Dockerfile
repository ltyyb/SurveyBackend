FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY ["version.txt", "./"]
COPY ["src/SurveyBackend/SurveyBackend.csproj", "src/SurveyBackend/"]
RUN dotnet restore "src/SurveyBackend/SurveyBackend.csproj"
COPY . .
WORKDIR /repo/src/SurveyBackend
RUN dotnet publish "SurveyBackend.csproj" -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
USER $APP_UID
EXPOSE 8080
EXPOSE 8081
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SurveyBackend.dll"]
