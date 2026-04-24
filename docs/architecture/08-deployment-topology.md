# Deployment Topology

## Local Development Topology

```mermaid
graph LR
    subgraph "Developer Machine"
        APP["RAG Navigator<br/>dotnet run<br/>http://localhost:5000"]
        FS["File System<br/>sample-data/<br/>docs/architecture/"]
    end

    subgraph "Azure (Shared Dev)"
        AOAI["Azure OpenAI<br/>(Standard S0)"]
        AIS["Azure AI Search<br/>(Basic)"]
    end

    APP -->|File I/O| FS
    APP -->|HTTPS + API Key| AOAI
    APP -->|HTTPS + API Key| AIS
```

**Authentication:** API keys stored in environment variables.
**Networking:** Direct HTTPS calls to Azure public endpoints.
**Cost:** Azure services are shared; only pay-per-use for OpenAI tokens.

## Azure Deployment Topology (Target)

```mermaid
graph TB
    subgraph "Internet"
        USER[Engineer's Browser]
    end

    subgraph "Azure Subscription"
        subgraph "Resource Group: rg-ragnavigator-prod"
            subgraph "Networking"
                FD[Azure Front Door<br/>WAF + TLS]
            end

            subgraph "Compute"
                ACA[Azure Container Apps<br/>2+ replicas<br/>Managed Identity]
            end

            subgraph "AI Services"
                AOAI[Azure OpenAI<br/>Standard S0]
                AIS[Azure AI Search<br/>Basic / Standard]
            end

            subgraph "Operations"
                KV[Azure Key Vault]
                AI[Application Insights]
                LA[Log Analytics<br/>Workspace]
            end
        end
    end

    USER -->|HTTPS| FD
    FD -->|Internal| ACA
    ACA -->|Managed Identity| AOAI
    ACA -->|Managed Identity| AIS
    ACA -->|Managed Identity| KV
    ACA -->|SDK| AI
    AI -->|Sink| LA

    style FD fill:#0078D4,color:#fff
    style ACA fill:#0078D4,color:#fff
    style AOAI fill:#0078D4,color:#fff
    style AIS fill:#0078D4,color:#fff
```

## Environment Separation

| Environment | Purpose | Azure Services | Auth |
|-------------|---------|----------------|------|
| **Local** | Development and testing | Shared dev Azure OpenAI + Search | API keys in env vars |
| **Dev** | Integration testing | Dedicated dev instances | Managed identity |
| **Staging** | Pre-production validation | Mirrors production config | Managed identity |
| **Production** | Live usage | Full production resources | Managed identity |

Each environment uses its own Azure AI Search index (via the `AZURE_SEARCH_INDEX_NAME` setting) to avoid cross-environment data contamination.

Terraform assigns the App Service managed identity the Azure OpenAI and Azure AI Search roles required by the application. It can also attach platform diagnostic settings to an existing Log Analytics workspace via `log_analytics_workspace_id`.

## Network Boundaries

### Current (Demo)

- All communication is over public HTTPS endpoints.
- No VNet, no private endpoints, no network isolation.
- Acceptable for a demo with non-sensitive engineering documents.

### Production Hardening

| Layer | Hardening |
|-------|-----------|
| **Ingress** | Azure Front Door with WAF rules, DDoS protection |
| **Compute → AI** | Private endpoints for Azure OpenAI and Azure AI Search |
| **Compute** | VNet-integrated Container Apps environment |
| **Secrets** | Key Vault with private endpoint, no API keys in config |
| **Egress** | NSG rules restricting outbound to known Azure service IPs |

### Future Network Diagram (Hardened)

```mermaid
graph TB
    subgraph "Internet"
        USER[Browser]
    end

    subgraph "Azure VNet"
        subgraph "Subnet: app"
            ACA[Container Apps]
        end

        subgraph "Subnet: private-endpoints"
            PE1[PE: Azure OpenAI]
            PE2[PE: Azure AI Search]
            PE3[PE: Key Vault]
        end
    end

    subgraph "Azure PaaS"
        AOAI[Azure OpenAI]
        AIS[Azure AI Search]
        KV[Key Vault]
    end

    USER -->|Azure Front Door| ACA
    ACA -->|Private Link| PE1
    ACA -->|Private Link| PE2
    ACA -->|Private Link| PE3
    PE1 -.-> AOAI
    PE2 -.-> AIS
    PE3 -.-> KV
```

## Infrastructure as Code

The current demo does not include IaC templates. For production:

| Tool | Scope |
|------|-------|
| **Bicep** | Azure resource provisioning (preferred for Azure-native) |
| **Terraform** | Alternative for multi-cloud teams |
| **Dockerfile** | Container image build |
| **GitHub Actions** | CI/CD pipeline |

### Minimum IaC Resources

1. Resource Group
2. Azure OpenAI account + model deployments
3. Azure AI Search service
4. Container Apps environment + app
5. Key Vault
6. Application Insights + Log Analytics
7. Managed Identity + RBAC role assignments
