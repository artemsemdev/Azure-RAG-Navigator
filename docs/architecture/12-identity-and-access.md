# Identity and Access

## Authentication Strategy

RAG Navigator supports two authentication modes for accessing Azure services, designed to be flexible for local development while being production-ready with managed identity.

Endpoint access is intentionally separate from Azure service access:

| Endpoint | Current Auth | Production Direction |
|----------|--------------|----------------------|
| `POST /api/chat` | Optional `X-Chat-Key` when `Security:ChatApiKey` / `CHAT_API_KEY` is configured | Entra ID bearer tokens with user/group claims |
| `POST /api/index/reindex` | `X-Admin-Key` when `Security:AdminApiKey` / `ADMIN_API_KEY` is configured; required outside Development | Entra ID admin role or managed operations job |
| Read-only pages / document list | Anonymous demo access | Entra ID or network-restricted internal app |

Set `Security:RequireChatApiKey=true` / `REQUIRE_CHAT_API_KEY=true` to make `/api/chat` fail closed if the chat API key is not configured.

### Local Development: API Keys

For local development, API keys are the simplest path to getting started:

```bash
export AZURE_OPENAI_API_KEY="your-key-here"
export AZURE_SEARCH_API_KEY="your-key-here"
```

The application checks for a non-empty `ApiKey` setting and uses `AzureKeyCredential` when one is present.

### Local Development: Azure CLI Login

Alternatively, developers can use `az login` to authenticate via `DefaultAzureCredential` without managing API keys:

```bash
az login
# Leave API key env vars empty — the app will use DefaultAzureCredential
```

This requires the developer's Azure AD account to have the appropriate RBAC roles (see below).

### Production: Managed Identity

In an Azure deployment, the application uses a system-assigned or user-assigned managed identity. No API keys are stored or rotated.

```
Container App → Managed Identity → Azure RBAC → Azure OpenAI / Azure AI Search
```

This is the recommended production approach because:
- No secrets to manage, rotate, or risk leaking.
- Identity is tied to the compute instance, not a shared credential.
- Permissions are enforced at the Azure RBAC layer.

## DefaultAzureCredential Chain

The `DefaultAzureCredential` class from `Azure.Identity` tries multiple authentication methods in order:

1. **Environment variables** (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`)
2. **Workload Identity** (for Kubernetes/AKS)
3. **Managed Identity** (system-assigned or user-assigned)
4. **Azure CLI** (`az login`)
5. **Azure PowerShell** (`Connect-AzAccount`)
6. **Visual Studio** credential
7. **Interactive browser** (last resort)

In the RAG Navigator implementation, `DefaultAzureCredential` is used only when no API key is configured. This makes the credential chain behavior explicit and predictable.

## RBAC Role Assignments

### Required for Azure OpenAI

| Role | Scope | Purpose |
|------|-------|---------|
| **Cognitive Services OpenAI User** | Azure OpenAI resource | Allows embedding generation and chat completion API calls |

This is the least-privilege role for consuming Azure OpenAI. It does not grant deployment management or model access modification.

### Required for Azure AI Search

| Role | Scope | Purpose |
|------|-------|---------|
| **Search Index Data Contributor** | Azure AI Search resource | Allows creating indexes, uploading documents, and querying |

This role grants both index management and data operations. For stricter separation:
- Use **Search Index Data Reader** for query-only workloads.
- Use **Search Service Contributor** for index schema management only.

### Terraform Assignments

The Terraform deployment assigns these roles to the App Service system-assigned managed identity:

| Role | Scope |
|------|-------|
| **Cognitive Services OpenAI User** | Azure OpenAI account |
| **Search Index Data Contributor** | Azure AI Search service |

### Manual Assignment Commands

```bash
# Assign roles to a managed identity
PRINCIPAL_ID="<managed-identity-object-id>"

# Azure OpenAI
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Cognitive Services OpenAI User" \
  --scope "/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/{name}"

# Azure AI Search
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Search Index Data Contributor" \
  --scope "/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Search/searchServices/{name}"
```

## Implementation Details

The DI registration in `DependencyInjection.cs` makes the auth decision explicit:

```csharp
// Azure OpenAI client
if (!string.IsNullOrEmpty(options.ApiKey))
    return new AzureOpenAIClient(endpoint, new AzureKeyCredential(options.ApiKey));

return new AzureOpenAIClient(endpoint, new DefaultAzureCredential());
```

The same pattern is used for `SearchIndexClient` and `SearchClient`. This means:
- API key auth: set the key in config → used immediately.
- Managed identity / az login: leave the key empty → `DefaultAzureCredential` is used.

## End-User Authentication

The current implementation provides API-key endpoint gates but does not yet model individual users. For production:

| Approach | Implementation |
|----------|---------------|
| **Azure AD / Entra ID** | Add `Microsoft.Identity.Web` middleware. Require bearer tokens on API endpoints. |
| **Document-level access** | Add user group claims to the token. Filter search results by user's groups. |
| **Audit logging** | Log the authenticated user ID with each query for compliance. |

## Security Considerations

1. **Never commit API keys.** The `appsettings.json` has empty placeholders. Real keys should only exist in environment variables or Key Vault.
2. **Rotate keys regularly** if using API key auth for development.
3. **Managed identity eliminates key rotation** — this is the primary reason to prefer it.
4. **RBAC follows least privilege** — each role grants only the permissions needed for the app's operations.
