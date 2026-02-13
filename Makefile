.PHONY: run build test clean restore

# Default target - run the desktop app
run:
	dotnet run --project CAP.Desktop/CAP.Desktop.csproj

# Build all projects
build:
	dotnet build

# Run tests
test:
	dotnet test UnitTests/UnitTests.csproj

# Clean build artifacts
clean:
	dotnet clean

# Restore dependencies
restore:
	dotnet restore

# Build and run
start: build run
