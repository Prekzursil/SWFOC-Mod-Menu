# SWFOC-Mod-Menu Makefile
# Primary build and verification targets for CI and local development.

.PHONY: help verify build test clean

# Default target
help:
	@echo "Available targets:"
	@echo "  make verify    - Run deterministic test suite (CI-safe, no runtime attach)"
	@echo "  make build     - Build the solution in Release mode"
	@echo "  make test      - Alias for verify"
	@echo "  make clean     - Clean build artifacts"

# Canonical verification command - runs deterministic tests only
# Excludes live tests that require a running SWFOC process
verify:
	@echo "[Makefile] Running deterministic test suite..."
	dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj \
		-c Release \
		--no-build \
		--filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"
	@echo "[Makefile] Verification complete."

# Build the solution
build:
	@echo "[Makefile] Building solution in Release mode..."
	dotnet restore SwfocTrainer.sln
	dotnet build SwfocTrainer.sln -c Release
	@echo "[Makefile] Build complete."

# Test alias
test: verify

# Clean build artifacts
clean:
	@echo "[Makefile] Cleaning build artifacts..."
	dotnet clean SwfocTrainer.sln -c Release
	dotnet clean SwfocTrainer.sln -c Debug
	@echo "[Makefile] Clean complete."
