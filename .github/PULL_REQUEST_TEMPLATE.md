<!--
This is the PR-as-session-log template. Every section is required.
Empty sections fail PR review.
See docs/AGENTIC_EXECUTION_RUNBOOK.md § PR-as-session-log.
-->

## Task: T-NN — <title from docs/tasks/T-NN.md>

### Scope statement

<!-- 2-3 sentences describing what this PR does. Match the task spec's "Context" section. -->

### Files changed

<!-- Bullet list. Each line: path/to/file — what changed and why. -->

-
-

### Behaviors preserved from existing code

<!--
Only required for T-02 and T-07.
Quote the regression-preservation list from the task spec and confirm each item.
For all other PRs, write "N/A — this task does not migrate existing behavior."
-->

### Acceptance criteria

<!--
Copy the entire checklist from docs/tasks/T-NN.md § Acceptance criteria.
Check every box. Do not delete unchecked boxes — fail the verify step instead.
-->

- [ ] (copy the task's acceptance criteria here, one per line)

### Verify output

```text
dotnet build:                 exit ?, "<last line of output>"
dotnet test:                  exit ?, "Passed: N; Failed: M"
forbidden-api-scan:           exit ?, "<last line>"
check-ddr-completeness:       exit ?, "<last line>"
check-coverage (if applicable): exit ?, "<last line>"
```

### New ADRs / DDRs

<!-- List any decision records added in this PR. If none, write "None". -->

### Risks and follow-ups

<!--
Any issues encountered during implementation.
Any TODOs filed.
Any deviation from the task spec (none expected — but if it happened, explain).
-->

### Out of scope

<!--
List anything you noticed during this task but did not do.
This is where future improvements get parked.
-->

---

**Reviewer checklist** (operator/architect, not the agent):

- [ ] PR scope matches the task spec deliverables exactly (no creep)
- [ ] No forbidden patterns introduced (scan output clean)
- [ ] No competing framework files created (no `spec.md`, `plan.md`, `tasks.md`, `PROJECT.md`, `STATE.md`, `constitution.md`, etc.)
- [ ] Hand-reviewed every file in the diff (mandatory for T-01 through T-07)
- [ ] PR description's Verify output matches CI's run
- [ ] If this is T-02 or T-07: every behavior in the regression-preservation list is confirmed by code inspection or test
- [ ] If this is T-07: hardware test checklist Sections A–E signed (see docs/hardware/T-23-checklist.md)
