# Architecture Overview

## System Diagram

```
┌─────────────────────────────────────────────────────────┐
│                      UNITY ENGINE                        │
├──────────────┬──────────────────────────┬────────────────┤
│  MonoBehaviour│      ECS (DOTS)          │  Hybrid Bridge │
│  (Managed)   │  (Unmanaged/Burst)       │                │
├──────────────┼──────────────────────────┼────────────────┤
│              │                          │                │
│  UI/         │  Systems/                │  Bootstrap/    │
│  ├─ HUD/     │  ├─ Movement/            │  ├─ Game       │
│  ├─ Panels/  │  ├─ Combat/              │  ├─ Economy    │
│  └─ Menus/   │  ├─ Work/                │  └─ AI         │
│              │  ├─ Training/            │                │
│  Input/      │  └─ Visibility/          │  Core/         │
│  ├─ RTSInput │                          │  ├─ Commands/  │
│  ├─ Select   │  AI/                     │  ├─ Components/│
│  └─ Camera   │  ├─ Brain                │  └─ Settings/  │
│              │  ├─ Managers/            │                │
│              │  └─ Behaviors/           │  Entities/     │
│              │                          │  ├─ Units/     │
│              │  Economy/                │  └─ Buildings/ │
│              │  ├─ Resources            │                │
│              │  └─ Population           │                │
└──────────────┴──────────────────────────┴────────────────┘
```

## Data Flow

```
Player Input                    AI Brain
     │                              │
     ▼                              ▼
RTSInputManager              AICommandAdapter
     │                              │
     └──────────┬───────────────────┘
                ▼
          CommandRouter
                │
     ┌──────────┼──────────┐
     ▼          ▼          ▼
  MoveCmd    BuildCmd   GatherCmd  ...
     │          │          │
     ▼          ▼          ▼
  Movement   Building    Mining
  System     System      System
```

## Component Organization

ECS Components live in `Core/Components/` in the **global namespace**:

| File | Contains |
|------|----------|
| `CoreComponents.cs` | Faction, Health, MoveSpeed, LineOfSight, etc. |
| `UnitComponents.cs` | UnitTag, MinerTag, BuilderTag, ArcherTag, etc. |
| `BuildingComponents.cs` | BuildingTag, HallTag, BarracksTag, ConstructionState, etc. |
| `CombatComponents.cs` | Target, Damage, AttackCooldown, Projectile, etc. |
| `ResourceComponents.cs` | IronMineTag, CrystalDepositTag, CarryingResources, etc. |
| `CommandComponents.cs` | MoveCommand, AttackCommand, BuildCommand, GatherCommand, etc. |

## Key Patterns

### Command Pattern
All player/AI actions go through `CommandRouter.cs` which attaches the appropriate ECS command component to entities. Systems then process these commands.

### Factory Pattern
`UnitFactory` and `BuildingFactory` handle entity creation with all required components. Never create entities directly - always use factories.

### State Machine (Implicit)
Unit behavior states are tracked via component presence (e.g., `MiningState`, `BuildingState`, `ReturningToBase`). Systems check for these components to determine behavior.
