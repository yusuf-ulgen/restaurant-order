# Design System & Application Shells (`docs/DESIGN-SYSTEM.md`)

## 1. Overview & Principles

The `restaurant-order` Design System (`packages/ui`) provides a unified, accessible, and responsive foundation for all user-facing product surfaces across the platform.

### Core Principles
1. **Accessibility First (WCAG 2.1 AA):** All interactive components enforce minimum 44px touch targets, visible focus outlines, keyboard navigation, screen reader semantics (`aria-*`), and full `prefers-reduced-motion` compliance.
2. **Neutral & High-Contrast Palette:** Built on a clean white/gray/black foundation with semantic status indicators (`success`, `warning`, `danger`, `primary`). Decorative gradients and distracting animations are strictly prohibited.
3. **Mobile-First & Safe-Area Aware:** Responsive design with strict mobile overflow prevention at 320px screen width and native support for iOS/Android safe areas (`env(safe-area-inset-*)`).
4. **Zero Heavy External Dependencies:** Pure React 19, TypeScript, and CSS custom properties (design tokens), ensuring minimal bundle size and maximum performance.
5. **Configuration-Driven Shells:** Shell layouts (header, footer, sidebar) are driven by type-safe configuration contracts (`types.ts`). Note: Dynamic settings persistence via admin panel will be implemented in Phase 4.

---

## 2. Design Token System

Design tokens are declared centrally in `packages/ui/src/tokens/` and exported via CSS custom properties (`tokens.css`):

| Token Category | CSS Variables | Purpose |
| :--- | :--- | :--- |
| **Colors** | `--ro-color-surface`, `--ro-color-background`, `--ro-color-border`, `--ro-color-primary`, `--ro-color-danger`, `--ro-color-success`, `--ro-color-warning` | Neutral surface & text colors, semantic accents |
| **Spacing** | `--ro-space-1` (4px) to `--ro-space-16` (64px) | Consistent 4px/8px spacing grid |
| **Typography** | `--ro-font-sans`, `--ro-font-size-*`, `--ro-font-weight-*`, `--ro-line-height-*` | Scalable type hierarchy |
| **Borders & Radii** | `--ro-radius-sm` (4px) to `--ro-radius-full` (9999px) | Component corners |
| **Elevation** | `--ro-shadow-sm`, `--ro-shadow-md`, `--ro-shadow-lg`, `--ro-shadow-xl` | Elevation and depth |
| **Z-Index** | `--ro-z-sticky` (1100), `--ro-z-drawer` (1300), `--ro-z-modal` (1400), `--ro-z-toast` (1600), `--ro-z-tooltip` (1700) | Layering hierarchy |
| **Touch Targets** | `--ro-touch-target-min` (44px), `--ro-touch-target-dense` (36px) | WCAG touch targets |
| **Safe Areas** | `--ro-safe-area-top`, `--ro-safe-area-bottom`, `--ro-safe-area-left`, `--ro-safe-area-right` | Mobile notch and gesture insets |

---

## 3. Component Inventory & Boundaries

### 3.1. Core Primitives (`packages/ui/src/components`)
- **`Button` & `IconButton`:** Standardized interactive triggers supporting variants (`primary`, `secondary`, `outline`, `ghost`, `danger`), loading spinner state (`aria-busy`), and accessible labels.
- **`Input`, `Textarea`, `Select`:** Form controls with invalid state indication (`aria-invalid`), disabled styling, and token integration.
- **`Checkbox`, `Switch`:** Binary inputs with accessible toggle states and focus indicators.
- **`Badge`, `Card`, `Divider`:** Visual grouping and status display primitives.
- **`Spinner`, `Skeleton`:** Loading state indicators respecting reduced-motion preferences.
- **`EmptyState`, `ErrorState`:** Honest state representations without fake data or misleading placeholders.
- **`FormField`, `FormError`:** Semantic form wrappers linking labels, inputs, and error messages (`aria-describedby`).
- **`VisuallyHidden`:** Screen-reader-only accessible text helper.
- **`ErrorBoundary`:** Client-side React error boundary with fallback UI.

### 3.2. Overlays & Dialogs (`packages/ui/src/overlay` & `components`)
- **`Portal`:** SSR-safe DOM portal mounting elements directly to `document.body`.
- **`useScrollLock`:** Reference-counted background scroll locking supporting nested/sequential overlays.
- **`useFocusTrap`:** Traps keyboard Tab navigation inside active overlays, handles initial focus (`initialFocusRef`), Escape dismissal, and focus restoration to trigger element upon close.
- **`Modal`:** Accessible dialog (`role="dialog"`, `aria-modal="true"`) for desktop and tablet flows.
- **`BottomSheet`:** Mobile-optimized slide-up sheet with drag handle indicator, internal scrolling, and safe-area padding.
- **`Drawer`:** Side panel supporting `left` and `right` placements.
- **`ConfirmationDialog`:** Explicit confirm/cancel dialog for destructive or critical actions.

### 3.3. Toast Notification System (`packages/ui/src/toast`)
- **`ToastProvider`:** React context provider managing toast queues, timers, and automatic dismissal.
- **`ToastViewport`:** Fixed container positioned via tokens, safe-area aware.
- **`ToastItem`:** Semantic notifications supporting `success`, `error`, `warning`, and `info` types with `role="status"` and `role="alert"`.
- **`useToast`:** Helper hook exposing `toast.success()`, `toast.error()`, `toast.warning()`, and `toast.info()`.

### 3.4. Application Shells & Layout (`packages/ui/src/shell`)
- **`AppShell`:** Top-level application layout coordinator supporting `customer`, `operations`, and `admin` variants.
- **`AppHeader`:** Responsive header with logo, title, subtitle, action slots, and mobile menu toggle.
- **`AppFooter`:** Accessible footer with copyright, business info, and safe internal/external links.
- **`Sidebar` & `SidebarSection`:** Collapsible desktop sidebar and mobile drawer integration.
- **`MobileNavigation`:** Fixed bottom navigation bar with safe-area support for mobile devices.
- **`PageHeader`, `PageContainer`, `ContentSection`:** Content hierarchy, responsive max-widths, and zero-overflow guarantees.
- **`SkipLink`:** Keyboard shortcut link to skip directly to `#main-content`.

---

## 4. Application Integration Matrix

| Surface | Shell Variant | Sidebar Behavior | Header Actions | Mobile Navigation |
| :--- | :--- | :--- | :--- | :--- |
| **`apps/customer-web`** | `customer` | **None** (Omitted) | Table status, active session badge | Direct action buttons + BottomSheet |
| **`apps/operations-web`** | `operations` | **None** (Mobile/tablet focused) | Shift status, waiter mode badge | Bottom `MobileNavigation` bar |
| **`apps/admin-web`** | `admin` | **Desktop Sticky + Mobile Drawer** | Admin badge, action buttons | Drawer opened via hamburger |

---

## 5. Governance & Future Scope

1. **Static vs. Dynamic:** In Phase 1, all sidebar, header, and navigation definitions are statically typed configurations (`types.ts`). Storing and persisting these configurations in a database will be delivered in Phase 4 (Restaurant Configuration).
2. **Role Authorization:** Sidebar item visibility is currently structural and does not constitute a security boundary. Role-based access control (RBAC) enforcement will be integrated in Phase 3.
