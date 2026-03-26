# Agent Pipeline Orchestrator

## Overview
This orchestrator runs a 4-agent pipeline to go from task description to merged code.

## Pipeline Flow
```
User Task Description
        │
        ▼
  ┌─────────────┐
  │ Task Intake  │──→ Creates GitHub Issue (#N)
  └─────────────┘
        │
        ▼
  ┌─────────────┐
  │ Spec Writer  │──→ Posts spec comment on #N
  └─────────────┘
        │
        ▼
  ┌─────────────┐
  │   Coder      │──→ Implements spec, creates PR (#M)
  └─────────────┘
        │
        ▼
  ┌─────────────┐     ┌──── APPROVED ──→ Done!
  │  Reviewer    │─────┤
  └─────────────┘     └── CHANGES_REQUESTED
        │                       │
        │               ┌──────┘
        │               ▼
        │         ┌─────────────┐
        └─────────│   Coder     │──→ Fixes issues
                  └─────────────┘
                        │
                        ▼
                  ┌─────────────┐
                  │  Reviewer   │──→ Re-reviews
                  └─────────────┘
                  (max 3 cycles)
```

## Execution Instructions

When the user says **"process task: <description>"** or similar, follow this pipeline:

### Step 1: Task Intake
Read `.github/agents/task-intake.md` for instructions.
- Parse the user's task description
- Explore the codebase briefly to understand context
- Create a GitHub issue using the API
- Capture the issue number

### Step 2: Spec Writer
Read `.github/agents/spec-writer.md` for instructions.
- Read the issue just created
- Explore relevant codebase files deeply
- Write a technical implementation spec
- Post it as a comment on the issue
- Confirm the spec with the user before proceeding

### Step 3: Coder
Read `.github/agents/coder.md` for instructions.
- Read the issue and spec comment
- Create a feature branch from develop
- Implement the spec step by step
- Commit with conventional commit messages
- Push and create a PR referencing the issue

### Step 4: Reviewer
Read `.github/agents/reviewer.md` for instructions.
- Read the PR diff
- Review against the checklist
- Post a review (APPROVE or REQUEST_CHANGES)

### Step 5: Review Cycle (if needed)
If the reviewer requests changes:
- The coder agent reads the review comments
- Makes the requested fixes
- Commits and pushes
- The reviewer re-reviews
- Maximum 3 review cycles before asking the user to intervene

## Environment
The GitHub token must be available. Check for it:
1. Environment variable `GH_TOKEN`
2. File `.env` in the project root (format: `GH_TOKEN=ghp_xxx`)

## User Checkpoints
The pipeline pauses for user confirmation at these points:
1. After issue creation: "Issue #N created. Proceed to spec writing?"
2. After spec is posted: "Spec posted on #N. Proceed to implementation?"
3. After PR creation: "PR #M created. Proceed to review?"
4. After review approval: "PR #M approved. Ready for merge."

## Partial Pipeline
Users can also run individual agents:
- "Create an issue for: <description>" → Task Intake only
- "Write a spec for issue #N" → Spec Writer only
- "Implement issue #N" → Coder only (reads existing spec)
- "Review PR #M" → Reviewer only
