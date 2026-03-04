# Development Workflow

## Branch Strategy

```
main ─────────────────────────────────── stable releases
  └── develop ────────────────────────── integration
        ├── feature/wall-system ──────── new features
        ├── fix/miner-pathfinding ────── bug fixes
        └── refactor/component-split ── restructuring
```

### Rules
- `main` receives merges from `develop` only after review
- `develop` is the integration branch - all feature/fix branches merge here
- Feature branches are short-lived (1-3 sessions ideally)
- Always branch from `develop`, never from `main`

## Working with Claude Code

### Session Workflow
1. Open Claude Code in the project directory
2. Claude reads `CLAUDE.md` automatically for project context
3. Reference GitHub issues: "Work on issue #12" or "Fix the bug in #5"
4. Claude will create feature branches, make changes, commit, and push
5. Create PRs via Claude: "Create a PR for this branch"

### Recommended Claude Code Commands
```bash
# Start working on a feature
"Create a feature branch for <description> and implement it"

# Fix a bug from an issue
"Fix issue #N"

# Review and refactor
"Review the code in <file> and suggest improvements"

# Create a PR
"Push this branch and create a PR"
```

## Commit Convention

Format: `<type>(<scope>): <description>`

### Types
| Type | Use For |
|------|---------|
| `feat` | New features, new systems, new components |
| `fix` | Bug fixes |
| `refactor` | Code restructuring without behavior change |
| `docs` | Documentation updates |
| `test` | Adding or updating tests |
| `chore` | Build, config, tooling changes |

### Scopes
`ai`, `combat`, `economy`, `ui`, `input`, `movement`, `building`, `mining`, `multiplayer`, `world`, `core`, `training`

### Examples
```
feat(economy): add veilstone resource tracking
fix(mining): miners stuck when deposit depletes
refactor(combat): split targeting into melee/ranged systems
docs(core): update component naming conventions
```

## Project Labels

| Label | Color | Description |
|-------|-------|-------------|
| `feature` | #0E8A16 | New feature or enhancement |
| `bug` | #D73A4A | Something isn't working |
| `task` | #0075CA | Development/refactoring task |
| `priority:high` | #B60205 | Must fix/implement soon |
| `priority:medium` | #FBCA04 | Normal priority |
| `priority:low` | #C5DEF5 | Nice to have |
| `system:ai` | #5319E7 | AI systems |
| `system:combat` | #E99695 | Combat systems |
| `system:economy` | #BFD4F2 | Economy/resources |
| `system:ui` | #D4C5F9 | User interface |
| `system:movement` | #006B75 | Movement/pathfinding |
| `system:multiplayer` | #1D76DB | Networking/multiplayer |
| `system:world` | #0E8A16 | World/terrain/fog |
| `blocked` | #000000 | Blocked by dependency |
| `needs-design` | #FBCA04 | Needs GDD clarification |

## Milestones

| Milestone | Goal |
|-----------|------|
| v0.8 - Core Loop | Basic gather-build-fight working |
| v0.9 - Cultures | Runai/Alanthor/Feraldis differentiation |
| v1.0 - Alpha | Full single-player skirmish |
| v1.1 - Multiplayer | Lockstep netcode working |
| v1.2 - Polish | Balance, UI, QoL improvements |
