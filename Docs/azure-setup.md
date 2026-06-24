# Running LexRAG against real Azure OpenAI

LexRAG runs keyless by default (hash embedder + extractive chat). These steps switch it to real Azure OpenAI models for semantic retrieval and real generation. Nothing here is required to build or test the project.

## 1. Create the Azure OpenAI resource

Azure Portal → Create a resource → Azure OpenAI → Create.

- Resource group: a new one (e.g. `rg-lexrag`).
- Region: pick one with broad model availability, such as East US 2 or Sweden Central.
- Pricing tier: Standard S0.

Creating the resource costs nothing; you pay per token consumed.

## 2. Deploy two models (Azure AI Foundry)

Open the resource and click **Go to Foundry portal** (it signs in through a separate OAuth flow, so you may pick your account again). If Foundry lands on a "create a project" screen, toggle **New Foundry** off and select the existing resource. Creating a new project also creates a new resource plus Application Insights, which you don't need here.

In the resource view: **Shared resources → Deployments → + Deploy model → Deploy base model**. Deploy one chat model and one embedding model.

Two things that are easy to trip on, both learned the hard way:

- **Availability and quota are per model and per deployment type.** An older model can be deprecated and simply not deployable (gpt-4o-mini was, deprecated 2026-03-31). A newer model may only offer Global Standard / DataZone, which can have zero default quota on a fresh subscription (the TPM slider sits at 0, or you get "insufficient quota").
- **What worked on a fresh subscription:** `gpt-4.1-mini` (chat) and `text-embedding-3-small` (embedding), both on **Global Standard**, with default quota (~100K–150K TPM). If a model/type shows zero quota, try a different current model or deployment type rather than fighting the slider. Prefer **Standard** or **Global Standard** (pay-per-call); avoid Provisioned and Batch for a PoC.

Note the **deployment names** you choose. The app maps to them by name (see step 4).

## 3. Get the endpoint and key

Resource → **Keys and Endpoint** → copy the **Endpoint** and **KEY 1**. The same key also appears on each deployment's detail page in Foundry.

## 4. Wire it through user-secrets

Never commit keys. Set them in the API project's secret store:

```bash
cd src/LexRag.Api
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:Key" "<KEY 1>"
dotnet user-secrets set "AzureOpenAI:ChatDeployment" "<chat deployment name>"        # e.g. gpt-4.1-mini
dotnet user-secrets set "AzureOpenAI:EmbeddingDeployment" "<embedding deployment name>" # e.g. text-embedding-3-small
```

The defaults are `gpt-4o-mini` / `text-embedding-3-small`, so override `ChatDeployment` whenever your chat deployment has a different name. Then:

```bash
dotnet run --project src/LexRag.Api
```

`GET /health` should report `AzureOpenAI` (and `pgvector` if you also set the Postgres connection string). The embedding dimension is fixed at 1536 (`Rag:EmbeddingDimensions`), which matches `text-embedding-3-small`; `MeaiEmbedder` requests that output size, so a `text-embedding-3-*` model lines up with the index schema.

## Cost

Billing is per token. A new account usually includes free credits, and creating the resource or a deployment costs nothing on its own. Set a budget alert (Cost Management → Budgets) with a low cap. Embedding the sample corpus once is a few cents; chat usage for CRAG and evaluation is cents.
