# Specs and ADRs

Write documents in English unless the project explicitly requires another language. Templates live in
`/etc/claude-code/templates/` (`spec.md`, `adr.md`); drop sections that don't apply instead of filling them with
filler.

## When

Spec — what is being built and how we know it is done. Write one for:
- a new feature or behaviour change visible to users or other systems (API, bot, integration, CLI);
- work that spans several components, takes more than about a day, or will need several PRs;
- a task whose requirements are ambiguous — the spec is where the open questions get answered.

Skip it for bug fixes, refactorings that keep behaviour, dependency bumps and small changes.

ADR — why a significant decision was made this way and not another. Write one when:
- the decision is expensive to reverse: storage, protocol, data format, public API or contract, auth/security model;
- there were real alternatives and someone will later ask "why not X?";
- it affects several components or all future work;
- it departs from a convention, e.g. a third-party package instead of a Curiosus library, or a new significant
  dependency.

Skip it for local implementation details and obvious choices.

## Where and how

- Specs: `docs/specs/YYYY-MM-DD-<slug>.md`. ADRs: `docs/adr/NNNN-<slug>.md`, numbered with the next free four-digit
  number. Implementation plans: `docs/plans/`. These paths override the defaults of any skill, including
  superpowers' `docs/superpowers/specs` and `docs/superpowers/plans`.
- Spec first: a spec goes in its own PR/MR (branch `spec/<slug>`) before any implementation code. Resolve every open
  question in that PR, then stop and wait for the owner to merge it. Implementation starts on a new branch from the
  updated main and links the spec. If the implementation has to deviate from the merged spec, update the spec in the
  same PR and call the change out in the description.
- The owner's merge is the approval: write specs as `Approved` and ADRs as `Accepted`. The implementation PR switches
  its spec to `Implemented`.
- An ADR ships in the PR that needs the decision — the spec PR or the implementation PR. Never rewrite an accepted
  ADR; supersede it with a new one and set the old one's status to `Superseded by NNNN`.
- Link a spec to the ADRs it introduces and each ADR to its spec.
