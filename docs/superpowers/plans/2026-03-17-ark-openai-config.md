# Configure Ark OpenAI-compatible Interface Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Configure the OpenDeepWiki project to use the Ark bytedance OpenAI-compatible API endpoint `https://ark.cn-beijing.volces.com/api/coding/v3` with API key `0aa6cee5-e9db-4485-b0d9-2fee2a980569` and model `ark-code-latest`.

**Architecture:** The project already supports OpenAI-compatible endpoints through environment variables or configuration file. We will update the development configuration file directly with the provided endpoint, API key, and model name. This approach keeps production configuration unchanged while enabling development to use the specified API.

**Tech Stack:** ASP.NET Core 8.0, Microsoft.Extensions.AI, OpenAI SDK

---

## Files to Modify

| File | Change |
|------|--------|
| `src/OpenDeepWiki/appsettings.Development.json` | Update AI.Endpoint, AI.ApiKey to match provided Ark API settings |
| `src/OpenDeepWiki/appsettings.json` | Update WikiGenerator.CatalogModel, WikiGenerator.ContentModel to `ark-code-latest` and add CatalogRequestType, ContentRequestType as `OpenAI` |

---

### Task 1: Update AI configuration in appsettings.Development.json

**Files:**
- Modify: `src/OpenDeepWiki/appsettings.Development.json:16-19`

- [ ] **Step 1: Update AI section with Ark endpoint and API key**

Change from:
```json
  "AI": {
    "Endpoint": "https://api.routin.ai/v1",
    "ApiKey": ""
  }
```

Change to:
```json
  "AI": {
    "Endpoint": "https://ark.cn-beijing.volces.com/api/coding/v3",
    "ApiKey": "0aa6cee5-e9db-4485-b0d9-2fee2a980569"
  }
```

- [ ] **Step 2: Verify the file is valid JSON**

- [ ] **Step 3: Commit the change**

```bash
git add src/OpenDeepWiki/appsettings.Development.json
git commit -m "config: update AI endpoint to Ark bytedance openai-compatible API"
```

---

### Task 2: Update WikiGenerator model configuration to use ark-code-latest

**Files:**
- Modify: `src/OpenDeepWiki/appsettings.json:42-43`

- [ ] **Step 1: Update CatalogModel and ContentModel to ark-code-latest**

Change from:
```json
  "WikiGenerator": {
    "CatalogModel": "MiniMax-M2.1",
    "ContentModel": "MiniMax-M2.1",
    "PromptsDirectory": "prompts",
    "MaxRetryAttempts": 3,
    "RetryDelayMs": 1000
  }
```

Change to:
```json
  "WikiGenerator": {
    "CatalogModel": "ark-code-latest",
    "ContentModel": "ark-code-latest",
    "CatalogRequestType": "OpenAI",
    "ContentRequestType": "OpenAI",
    "PromptsDirectory": "prompts",
    "MaxRetryAttempts": 3,
    "RetryDelayMs": 1000
  }
```

- [ ] **Step 2: Verify the file is valid JSON**

- [ ] **Step 3: Commit the change**

```bash
git add src/OpenDeepWiki/appsettings.json
git commit -m "config: update wiki generator models to ark-code-latest"
```

---

### Task 3: Build the project to verify configuration

**Files:**
- Solution: `OpenDeepWiki.sln`

- [ ] **Step 1: Run dotnet build**

Run:
```bash
dotnet build OpenDeepWiki.sln
```

Expected: Build succeeds with no errors.

---

### Task 4: Start the backend API

**Files:**
- Project: `src/OpenDeepWiki/OpenDeepWiki.csproj`

- [ ] **Step 1: Run the backend project**

Run:
```bash
dotnet run --project src/OpenDeepWiki/OpenDeepWiki.csproj
```

Expected: Application starts successfully, listens on HTTP ports, no configuration errors.
