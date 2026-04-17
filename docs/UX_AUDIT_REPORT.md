# PoWatch UX Audit Report
**Prepared by:** UX Architect  
**Date:** April 17, 2026  
**Application:** PoWatch - Clinical Activity Monitoring System  
**User Context:** "Stat Geek" who wants to analyze, understand trends, and aggregate data via local/cloud LLM

---

## STEP 1: RESEARCH PHASE - ANALYSIS

### 1.1 Current Data Flow Analysis

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        DATA FLOW DIAGRAM                                │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  Camera/Sensor ──► POST /api/observer/ingest ──► [Gate Check] ──►      │
│       │                                               │                 │
│       │                              ┌────────────────┴────────────────┐│
│       │                              ▼                                 │
│       │                    ┌─────────────────────┐                     │
│       │                    │  Processing Pipeline │                     │
│       │                    │  • Subject Resolve  │                     │
│       │                    │  • Clinical Parse   │                     │
│       │                    │  • Redundancy Gate │                     │
│       │                    │  • Persist Event   │                     │
│       │                    └─────────┬───────────┘                     │
│       │                              │                                 │
│       │         ┌───────────────────┼───────────────────┐             │
│       │         ▼                   ▼                   ▼             │
│       │  ┌────────────┐    ┌──────────────┐    ┌─────────────┐        │
│       │  │ Azure      │    │ Azure Blob   │    │ Subject     │        │
│       │  │ Table      │    │ Storage      │    │ Cache       │        │
│       │  │ Storage    │    │ (images)     │    │ (LastActivity)        │
│       │  └────────────┘    └──────────────┘    └─────────────┘        │
│       │         │                   │                   │             │
│       │         └───────────────────┴───────────────────┘             │
│       │                             │                                 │
│       │                             ▼                                 │
│       │              ┌──────────────────────────┐                      │
│       │              │    Client Polling       │                      │
│       │              │    GET /api/observer/state                     │
│       │              │    (every N seconds)     │                      │
│       │              └────────────┬─────────────┘                      │
│       │                           │                                    │
│       ▼                           ▼                                    │
│  Live Camera Feed ◄──── UI Updates ◄──── ObserverHub.razor             │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 1.2 Redundant States Identified

| State | Location | Issue | Recommendation |
|-------|----------|-------|----------------|
| **Duplicate Timeline** | Archives.razor + ObserverHub.razor | Both render `activity-list` with identical structure | Consolidate to single component |
| **Metrics Duplication** | ObserverHub metrics grid + HUD bar | Session duration, status, latest activity shown in 2 places | Merge into single "at-a-glance" panel |
| **Inference Stats Overlap** | ObserverHub.razor lines 246-305 | 11 metrics shown in expandable section, but key metrics also in hero section | Progressive disclosure - show critical 3-4, hide rest |
| **Subject List** | IdentityNexus + LiveDashboard | Both show subject cards, but different formats | Create unified subject component |
| **Loading States** | Multiple pages | Skeleton loaders + progress bars + spinners inconsistent | Standardize loading UX |

### 1.3 Unnecessary Nested Views

| Nested View | Current Depth | Problem | Recommendation |
|-------------|---------------|---------|----------------|
| HUD Expanded Section | 3 clicks away | User must expand HUD, then find metrics | Move to dedicated diagnostics panel or tooltip |
| Developer Tools | Behind flag + toggle | Not accessible without `DeveloperModeEnabled` | Make conditionally visible based on feature flag, not hidden |
| Merge Form | Accordion inside accordion | Identity page has merge collapsed by default | Consider wizard or slide-over modal |
| Handoff Brief | Nested expandable inside Archives | Multiple layers: toolbar → expand → generate → view | Surface brief as primary action |

### 1.4 Primary User Action Per Screen

| Screen | Primary Action | Current UX Barriers |
|--------|----------------|---------------------|
| **ObserverHub** | Start/Stop monitoring + view live feed | Dense metrics, hidden controls, verbose descriptions |
| **Archives** | Browse daily timeline + get narrative | Date picker buried, handoff workflow multi-step |
| **LiveDashboard** | View all subjects at a glance | Auto-refresh hidden, cards require click-through |
| **IdentityNexus** | Rename subjects | Double-click hint confusing, inline edit fragile |
| **Diagnostics** | Check system health | Two panels, auto-refresh toggle unclear |

---

## STEP 3: SIMPLIFICATION ROADMAP

### 3.1 CONSOLIDATION: UI Elements to Merge

| Current Element | Target Element | Merge Strategy |
|----------------|----------------|----------------|
| ObserverHub Hero Metrics (3 cards) + HUD Bar + Stats Panel | **Single "Command Center" Panel** | Create unified metrics dashboard showing 4-5 critical stats |
| Archives Narrative + Handoff Brief Panel | **Unified Summary Section** | Merge into "Daily Summary" with narrative + AI brief in tabs |
| LiveDashboard Cards + IdentityNexus Grid | **Subject Library Component** | Single reusable component with view modes |
| Expandable HUD + Inference Analytics | **Drill-Down Modal** | Click to see full analytics, not expandable section |

### 3.2 PROGRESSIVE DISCLOSURE: What to Hide Until Needed

| Information | Current State | Proposed State | Trigger |
|------------|---------------|----------------|---------|
| FP16 Fallback indicator | Always visible in metrics | Hide unless active | Only show when `Fp16FallbackUsed == true` |
| EMA Drift score | Always in HUD | Hide unless user expands "Advanced" | Collapsed by default |
| P95 Latency | Always in metrics | Hide, show in tooltip of main latency | On hover |
| Developer Tools | Behind flag + toggle | Show icon button when flag enabled | Feature flag controlled |
| Full timeline | Always expanded | Show 10 items, "Load more" | Scroll or button |
| Merge form | Collapsed accordion | Modal or slide-over | Explicit "Merge" action |

### 3.3 THE ONE-CLICK RULE: Core Value in Fewest Steps

| User Goal | Current Steps | Proposed Steps | Improvement |
|-----------|---------------|----------------|-------------|
| Start monitoring | 3 clicks (navigate → find button → click Start) | **1 click** (Start button on any page via global action bar) | 67% reduction |
| View daily summary | Navigate to Archives → Pick date → See narrative | **1 click** (Dashboard widget shows today's summary) | Dashboard shows summary without navigation |
| Rename subject | Navigate to Identity → Find subject → Double-click → Type → Press Enter | **2 clicks** (Quick edit icon → Inline edit) | Remove double-click requirement |
| Get handoff brief | Archives → Expand toolbar → Expand handoff → Click Generate | **1 click** (Floating action button "Generate Brief") | Streamlined workflow |
| Check subject status | Navigate to LiveDashboard → Find card → Click | **0 clicks** (ObserverHub sidebar shows all subjects) | Real-time sidebar widget |

---

## TOP 5 UI ENHANCEMENTS (1-5)

### 1. **Unified Command Center Panel** (ObserverHub Overhaul)
**Current State:** 3 metric cards + HUD bar + stats panel scattered across screen  
**Proposed State:** Single "Command Center" card with 5 critical metrics + expandable advanced section

```
┌──────────────────────────────────────────────────────────────┐
│  COMMAND CENTER                                     [−/+]   │
├──────────────────────────────────────────────────────────────┤
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐    │
│  │ STATUS   │  │ RECORDING│  │ SUBJECTS │  │ TODAY    │    │
│  │ ● LIVE   │  │ 02:34:15 │  │ 3 active │  │ 47 events│    │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘    │
├──────────────────────────────────────────────────────────────┤
│  [ START MONITORING ]  [ VIEW SUMMARY ]  [ QUICK ACTIONS ▾]│
└──────────────────────────────────────────────────────────────┘
```

**Impact:** Reduces cognitive load, surfaces most important data immediately  
**Blast Radius:** Low - CSS/layout changes only, no logic changes

---

### 2. **Floating Action Button (FAB) for Primary Actions**
**Current State:** Actions buried in page-specific toolbars  
**Proposed State:** Floating button in bottom-right corner with context-aware actions

| Context | FAB Options |
|---------|-------------|
| ObserverHub (monitoring) | Stop, Pause, Quick Capture |
| ObserverHub (idle) | Start, View Summary |
| Archives | Generate Brief, Download Report |
| Any page | Quick Subject Switch, Help |

**Impact:** One-click access to most-used actions from anywhere  
**Blast Radius:** Low - New UI component, existing buttons remain functional

---

### 3. **Activity Stream Widget (Global)**
**Current State:** Activity list only on ObserverHub  
**Proposed State:** Collapsible sidebar widget showing live activity stream

```
┌─────────────────────┐
│  LIVE ACTIVITY  ●   │
├─────────────────────┤
│  14:32:15  Patient A │
│     Walking          │
│  14:31:42  Patient B │
│     Sitting ⚠ OUTLIER│
│  14:30:58  Patient A │
│     Standing         │
│  14:28:11  Patient C │
│     Lying down       │
├─────────────────────┤
│  [View Full Timeline]│
└─────────────────────┘
```

**Impact:** Users see activity without switching pages  
**Blast Radius:** Medium - Requires real-time data subscription, WebSocket consideration

---

### 4. **Modern Card Design System**
**Current State:** RadzenCard with inconsistent padding, varying header styles  
**Proposed State:** Unified card design with consistent:
- Corner radius (12px)
- Shadow elevation (subtle gradient shadow)
- Header typography hierarchy
- Micro-interactions on hover/focus

**Visual Example:**
```css
.card-modern {
  border-radius: 12px;
  background: linear-gradient(145deg, rgba(30,30,50,0.9), rgba(20,20,35,0.95));
  box-shadow: 0 4px 20px rgba(0,0,0,0.3), inset 0 1px 0 rgba(255,255,255,0.05);
  border: 1px solid rgba(111, 61, 227, 0.15);
  transition: transform 0.2s ease, box-shadow 0.2s ease;
}

.card-modern:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 30px rgba(56, 255, 168, 0.1), 0 4px 20px rgba(0,0,0,0.4);
}
```

**Impact:** Modern, cohesive visual identity  
**Blast Radius:** Low - CSS changes, no logic impact

---

### 5. **Smart Date Navigation (Archives)**
**Current State:** Date picker + prev/next arrows buried in toolbar  
**Proposed State:** Date selector integrated into page header with quick presets

```
┌──────────────────────────────────────────────────────────────┐
│  ARCHIVES                        [Today] [Yesterday] [Week] │
├──────────────────────────────────────────────────────────────┤
│  ◀ April 16, 2026 ▶                                        │
└──────────────────────────────────────────────────────────────┘
```

**Impact:** Faster date selection, reduces toolbar complexity  
**Blast Radius:** Low - UI changes, existing date picker logic unchanged

---

## TOP 5 UX IMPROVEMENTS (6-10)

### 6. **Contextual Empty States with CTAs**
**Current State:** Generic "No records to display" messages  
**Proposed State:** Actionable empty states with clear next steps

| Empty State | Current Message | Proposed Message + CTA |
|-------------|-----------------|------------------------|
| Archives (no events) | "No observations recorded for this day" | "No activity recorded on [date]. Start monitoring to capture events." → [Go to Observer Hub] |
| Identity (no subjects) | "No subjects identified yet" | "Waiting for subjects to appear. Monitoring is [active/idle]." → [View Monitor] |
| LiveDashboard (no subjects) | "No subjects observed yet" | "No subjects detected yet. Enable monitoring to begin detection." |

**Impact:** Guides users to core value faster  
**Blast Radius:** Low - Content and link changes only

---

### 7. **Inline Rename with Single Click**
**Current State:** Double-click required OR edit button + modal  
**Proposed State:** Single-click on edit icon enables inline edit

```razor
<!-- Current (IdentityNexus.razor line 57-60) -->
<span class="subject-name" @ondblclick="@(() => BeginInlineEdit(item))" ...>

<!-- Proposed -->
<span class="subject-name">@item.DisplayName</span>
<RadzenButton Icon="edit" Click="@(() => BeginInlineEdit(item))" />
```

**Impact:** Removes learnability barrier of double-click  
**Blast Radius:** Low - Single line change

---

### 8. **Monitoring State as First-Class UI Element**
**Current State:** Status shown in badge, HUD bar, hero section (3 places)  
**Proposed State:** Global monitoring indicator + quick controls

```
┌─────────────────────────────────────────────────────────────────────┐
│ [●] MONITORING  │  ObserverHub  │  Archives  │  Identity  │ Diag  │
└─────────────────────────────────────────────────────────────────────┘
```

| State | Indicator |
|-------|-----------|
| Idle | ○ Gray "Ready" |
| Monitoring | ● Green "Live" + pulsing |
| Paused | ◐ Yellow "Paused" |
| Error | ● Red "Alert" |

**Impact:** User always knows system state without navigating  
**Blast Radius:** Medium - Layout changes, state management unchanged

---

### 9. **Quick Actions Menu (ObserverHub)**
**Current State:** Start/Stop buttons + GPU dropdown + Model dropdown + Poll interval slider all visible  
**Proposed State:** Primary button prominent, advanced options in "Settings" slide-over

```
┌──────────────────────────────────────────────────────────────┐
│                                                              │
│              ┌──────────────────────────────┐                │
│              │                              │                │
│              │      [ ▶ START ]            │                │
│              │                              │                │
│              └──────────────────────────────┘                │
│                                                              │
│              [ ⚙ Quick Settings ]                            │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

**Quick Settings Slide-over:**
- Inference Model (dropdown)
- GPU Preference (dropdown)
- Poll Interval (slider)
- Save Significant Images (toggle)

**Impact:** Reduces initial complexity, expert users still have access  
**Blast Radius:** Low - UI reorganization, settings logic unchanged

---

### 10. **Smart Narrative Generation (Archives)**
**Current State:** Narrative: "Room recorded N events. Primary: {name}. Dominant: {activity}. Outliers: N."  
**Proposed State:** Expandable, structured narrative with trend insights

```
┌──────────────────────────────────────────────────────────────┐
│  DAILY SUMMARY — April 17, 2026                    [Expand] │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  47 events recorded across 4 subjects.                     │
│                                                              │
│  ▸ Most Active: Patient A (23 events)                       │
│  ▸ Common Activity: Walking (41%)                           │
│  ▸ Outliers: 2 events flagged for review                   │
│  ▸ Trend: ↑ Activity increased 15% vs yesterday            │
│                                                              │
│  [Generate AI Brief]                    [View Timeline →]   │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

**Impact:** Makes data actionable, highlights trends automatically  
**Blast Radius:** Medium - API may need enhancement for trend data

---

## BLAST RADIUS ASSESSMENT

| Change | Complexity | Downstream Dependencies | Risk Level |
|--------|------------|-------------------------|------------|
| **Unified Command Center** | Low | None | 🟢 Safe |
| **FAB for Primary Actions** | Low | None | 🟢 Safe |
| **Activity Stream Widget** | Medium | Real-time data (WebSocket/poll) | 🟡 Moderate |
| **Modern Card Design** | Low | None | 🟢 Safe |
| **Smart Date Navigation** | Low | None | 🟢 Safe |
| **Contextual Empty States** | Low | None | 🟢 Safe |
| **Inline Rename Fix** | Low | None | 🟢 Safe |
| **Global Monitoring Indicator** | Medium | State service | 🟡 Moderate |
| **Quick Actions Menu** | Low | None | 🟢 Safe |
| **Smart Narrative Enhancement** | Medium | API/DTO changes for trend data | 🟡 Moderate |

---

## IMPLEMENTATION PRIORITY MATRIX

```
                    IMPACT
          Low          │          High
          │             │             │
    ┌─────┴─────┐       │       ┌─────┴─────┐
    │           │       │       │           │
LOW │  Keep     │   QUICK     │  3, 4, 9   │
    │  Running  │   WINS      │           │
    │           │       │       │           │
────┼───────────┼───────┼───────┼───────────┼────
    │           │       │       │           │
HIGH│           │   STRATEGY    │  1, 2, 6,  │
    │           │       │       │  7, 8, 10  │
    │           │       │       │           │
    └───────────┘       │       └───────────┘
                         │
                    EFFORT
```

### Quick Wins (Implement First)
1. **#4** - Modern Card Design (CSS only)
2. **#6** - Contextual Empty States (Content only)
3. **#7** - Inline Rename Fix (1 line change)
4. **#9** - Quick Actions Menu (UI reorganization)

### Strategic Initiatives (High Impact)
1. **#1** - Unified Command Center (Major ObserverHub redesign)
2. **#2** - Floating Action Button (Global UX pattern)
3. **#8** - Global Monitoring Indicator (App-wide state visibility)
4. **#10** - Smart Narrative Enhancement (Data aggregation)

---

## SUMMARY

Based on your priorities (stat-focused clinician, trend analysis, LLM summarization):

1. **Reduce ObserverHub complexity** - The 11 inference metrics overwhelm users. Condense to 4-5 key stats, surface trends automatically
2. **Unify data presentation** - Activity timeline appears in 2 places with identical structure. Create single source
3. **Surface insights proactively** - Instead of requiring users to derive meaning, show "Most Active Subject", "Dominant Activity", "Trend vs Yesterday"
4. **Simplify navigation to value** - Handoff Brief should be 1 click, not buried in toolbar
5. **Maintain power-user access** - Advanced settings remain available via "Quick Settings" or expandable sections

**Next Steps:**
1. Review and prioritize these recommendations
2. Select top 3 for initial implementation
3. Conduct user testing with simplified prototype
