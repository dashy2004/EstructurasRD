---
name: Memoria Plus
colors:
  surface: '#fcf8fb'
  surface-dim: '#dcd9dc'
  surface-bright: '#fcf8fb'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f6f3f5'
  surface-container: '#f0edef'
  surface-container-high: '#eae7ea'
  surface-container-highest: '#e4e2e4'
  on-surface: '#1b1b1d'
  on-surface-variant: '#434751'
  inverse-surface: '#303032'
  inverse-on-surface: '#f3f0f2'
  outline: '#737782'
  outline-variant: '#c3c6d2'
  surface-tint: '#305ea4'
  primary: '#064287'
  on-primary: '#ffffff'
  primary-container: '#2c5aa0'
  on-primary-container: '#bfd3ff'
  inverse-primary: '#abc7ff'
  secondary: '#5d5e60'
  on-secondary: '#ffffff'
  secondary-container: '#dfdfe1'
  on-secondary-container: '#616365'
  tertiary: '#673800'
  on-tertiary: '#ffffff'
  tertiary-container: '#884c00'
  on-tertiary-container: '#ffc89a'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#d7e2ff'
  primary-fixed-dim: '#abc7ff'
  on-primary-fixed: '#001b3f'
  on-primary-fixed-variant: '#0e458a'
  secondary-fixed: '#e2e2e4'
  secondary-fixed-dim: '#c6c6c8'
  on-secondary-fixed: '#1a1c1d'
  on-secondary-fixed-variant: '#454749'
  tertiary-fixed: '#ffdcc0'
  tertiary-fixed-dim: '#ffb877'
  on-tertiary-fixed: '#2e1600'
  on-tertiary-fixed-variant: '#6c3a00'
  background: '#fcf8fb'
  on-background: '#1b1b1d'
  surface-variant: '#e4e2e4'
typography:
  h1:
    fontFamily: Inter
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
    letterSpacing: -0.01em
  h2:
    fontFamily: Inter
    fontSize: 18px
    fontWeight: '600'
    lineHeight: 24px
    letterSpacing: -0.01em
  body-sm:
    fontFamily: Inter
    fontSize: 13px
    fontWeight: '400'
    lineHeight: 20px
  data-tabular:
    fontFamily: JetBrains Mono
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
  label-caps:
    fontFamily: Inter
    fontSize: 11px
    fontWeight: '700'
    lineHeight: 16px
    letterSpacing: 0.05em
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  row-height: 28px
  input-height: 32px
  container-padding: 16px
  element-gap: 8px
  grid-gutter: 12px
---

## Brand & Style

This design system is engineered for high-stakes structural calculation environments where precision is paramount and cognitive load must be minimized. The brand personality is clinical, authoritative, and strictly functional, prioritizing data integrity over decorative flourishes.

The visual style is **Corporate / Modern** with a lean toward **Minimalism**. It employs a "flat" aesthetic that eliminates depth cues like gradients and heavy shadows in favor of a rigorous grid and clear typographic hierarchy. The interface is designed to evoke a sense of professional reliability, mirroring the exactitude of the engineering blueprints and mathematical models it supports.

## Colors

The palette is anchored by a neutral gray base that provides a low-distraction canvas for complex data. **Cobalt Blue** serves as the primary accent, used sparingly to denote primary actions and active states without overwhelming the user's focus.

Semantic colors are calibrated for high legibility against both the light (#F5F5F7) and dark (#1C1C1E) backgrounds. Success, warning, and error states utilize desaturated yet distinct tones to ensure they are visible within high-density tables and schematic views without causing visual fatigue.

## Typography

This design system utilizes a dual-font strategy to separate interface controls from engineering data. **Inter** is the primary UI typeface, chosen for its exceptional readability in compact layouts and neutral character. It handles all navigation, headers, and instructional text.

For structural calculations, dimensions, and tabular data, **JetBrains Mono** (or a similar technical monospaced font) is employed. The fixed-width nature of the characters ensures that columns of numbers remain perfectly aligned, facilitating rapid scanning and comparison of engineering values.

## Layout & Spacing

The layout utilizes a **Fixed Grid** model for specialized calculation panels and a **Fluid Grid** for secondary data visualizations. The system is optimized for high-density information display, allowing engineers to view large datasets without excessive scrolling.

A strict vertical rhythm is maintained with 28px row heights for data lists and 32px for interactive inputs. This compact spacing requires precise hit targets and clear hover states to maintain usability. Content should be grouped into logical modules with consistent 16px internal padding.

## Elevation & Depth

To maintain a "flat" engineering aesthetic, this design system avoids traditional drop shadows. Depth is conveyed exclusively through **low-contrast outlines** and **tonal layers**.

1.  **Base Layer:** The primary background color (#F5F5F7).
2.  **Surface Layer:** Secondary containers use a 1px solid border (#D1D1D6) to define boundaries.
3.  **Active Layer:** Selected or focused elements use the Cobalt Blue accent for borders or a subtle 2px inset stroke.
4.  **Overlays:** Modals or tooltips use a single-pixel hairline border with a very soft, desaturated 4px ambient blur to separate them from the calculation grid.

## Shapes

The shape language is architectural and rigid. A **minimal rounding of 4px** (Soft) is applied to buttons, input fields, and containers to prevent the UI from feeling overly sharp while maintaining a professional, technical appearance. 

Status indicators and badges may use a slightly higher radius for distinction, but structural components must adhere to the 4px standard to ensure they align perfectly within the dense grid system.

## Components

### Buttons & Inputs
Buttons are flat with solid color fills for primary actions and 1px borders for secondary actions. Input fields are strictly 32px high, utilizing a white background in light mode to provide high contrast against the gray UI base. Active states are indicated by a Cobalt Blue 2px border.

### Data Tables
The core of the system. Tables use 28px rows with subtle 1px horizontal dividers. Columns containing numeric data must use monospaced typography and right-alignment to ensure decimal points and magnitudes are easily comparable.

### Chips & Badges
Small, rectangular tags with 2px rounding. These are used for status (e.g., "Validated," "Error") and should use desaturated semantic background colors with high-contrast text.

### Interactive Schematic Areas
Calculations often correspond to visual models. These areas should be framed in the same 1px border as cards, with a slightly darker neutral background to provide a clear "viewport" feel for structural diagrams.