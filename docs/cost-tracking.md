# Cost Tracking

Model-backed features are cost-sensitive. The live system records token usage in triage-ledger `ModelCall` events and charges attempt budgets with first-class `BudgetEvent.tokens_delta` rows.

## Live Inputs

- input tokens;
- output tokens;
- total tokens;
- usage source (`provider` or `estimate`);
- provider;
- model;
- route ID;
- request count derived from `ModelCall` rows.

## Planned Rollup

Cost rollup is planned under IC-BL-014. The intended source data is:

- `ModelCall` rows in `incidentcompass.triage_ledger` for usage, provider and model;
- the dormant `incidentcompass.ai_model_pricing` table for effective-dated pricing;
- the dormant `AiCostEstimator`, `PricingRecord` and `IPricingRepository` code retained for that backlog item.

The current system does not write estimated cost to a separate request-log table and does not expose a usage dashboard. The mock models still seed zero-cost USD pricing records so future rollup work can remain deterministic for local runs and tests.

## Pricing Table

`incidentcompass.ai_model_pricing` records include:

- provider;
- model;
- currency;
- input token price;
- output token price;
- embedding token price where applicable;
- effective dates.

## Quotas

Future quota examples:

- max requests per user per day;
- max tokens per user per day;
- max estimated cost per user per month;
- max requests per tenant per day, optional.
