# <Feature title>

- Status: Approved
- Date: YYYY-MM-DD
- Issue: <link, if any>
- ADRs: <links to ADRs this spec relies on or introduces>

## Problem and goal

Who has the problem, what it is, and what outcome counts as solved. One or two paragraphs.

## Scope

In scope:

- <...>

Out of scope (non-goals):

- <...>

## Requirements

Functional:

1. <The system must ...>

Non-functional (performance, security, compatibility, observability), only where they matter:

1. <...>

## Acceptance scenarios

```gherkin
Scenario: <name>
  Given <state>
  When <action>
  Then <observable result>
```

## Contracts

Public API endpoints, messages, CLI commands, configuration, data model and migrations: the shapes other code or
people will depend on. Omit sections that don't apply.

## Edge cases and failure modes

- <input, state, or dependency failure> → <expected behaviour>

## Test plan

Which scenarios are covered by unit, integration and end-to-end tests, and what infrastructure they need.

## Rollout

Migrations, feature flags, backward compatibility, deployment order. Omit if nothing special is needed.

## Open questions

Must be empty before the spec PR is merged.
