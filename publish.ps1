# Update version in csproj
# <GeneratePackageOnBuild>true</GeneratePackageOnBuild> in csproj means it will create a .nupkg in bin/Release/
# So, just build in Release 
dotnet build -c Release