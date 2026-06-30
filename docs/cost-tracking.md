# Cost Tracking

Model-backed features are cost-sensitive. Cost estimation is computed and stored at request-log time so it stays reproducible even after pricing changes.

## Inputs

- input tokens;
- output tokens;
- embedding tokens;
- model pricing;
- request count.

## Pricing

The observability schema stores pricing records in `incidentcompass.ai_model_pricing`. Records include:

- provider;
- model;
- currency;
- input token price;
- output token price;
- embedding token price where applicable;
- effective dates.

When a model request is logged, estimated cost is calculated from the pricing
record effective at the request timestamp and stored on the request log. Storing
the estimate at write time keeps historical cost data reproducible after later
pricing changes, even though no reporting endpoint reads it back yet.

The local mock models are seeded with zero-cost USD pricing for deterministic
local runs and tests.

## Quotas

Future quota examples:

- max requests per user per day;
- max tokens per user per day;
- max estimated cost per user per month;
- max requests per tenant per day, optional.
