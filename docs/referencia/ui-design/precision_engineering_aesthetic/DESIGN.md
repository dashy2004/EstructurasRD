---
name: Precision Engineering Aesthetic
colors:
  surface: '#f9f9ff'
  surface-dim: '#d9d9e0'
  surface-bright: '#f9f9ff'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f3f3fa'
  surface-container: '#ededf4'
  surface-container-high: '#e8e7ee'
  surface-container-highest: '#e2e2e8'
  on-surface: '#1a1c20'
  on-surface-variant: '#434751'
  inverse-surface: '#2f3035'
  inverse-on-surface: '#f0f0f7'
  outline: '#737782'
  outline-variant: '#c3c6d2'
  surface-tint: '#305ea4'
  primary: '#064287'
  on-primary: '#ffffff'
  primary-container: '#2c5aa0'
  on-primary-container: '#bfd3ff'
  inverse-primary: '#abc7ff'
  secondary: '#585f6c'
  on-secondary: '#ffffff'
  secondary-container: '#dce2f3'
  on-secondary-container: '#5e6572'
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
  secondary-fixed: '#dce2f3'
  secondary-fixed-dim: '#c0c7d6'
  on-secondary-fixed: '#151c27'
  on-secondary-fixed-variant: '#404754'
  tertiary-fixed: '#ffdcc0'
  tertiary-fixed-dim: '#ffb877'
  on-tertiary-fixed: '#2e1600'
  on-tertiary-fixed-variant: '#6c3a00'
  background: '#f9f9ff'
  on-background: '#1a1c20'
  surface-variant: '#e2e2e8'
typography:
  h1:
    fontFamily: Inter
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
    letterSpacing: -0.01em
  h2:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '600'
    lineHeight: 24px
    letterSpacing: -0.01em
  body-md:
    fontFamily: Inter
    fontSize: 13px
    fontWeight: '400'
    lineHeight: 18px
    letterSpacing: 0em
  label-sm:
    fontFamily: Inter
    fontSize: 11px
    fontWeight: '500'
    lineHeight: 16px
    letterSpacing: 0.02em
  data-tabular:
    fontFamily: JetBrains Mono
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
    letterSpacing: 0em
  data-header:
    fontFamily: Inter
    fontSize: 11px
    fontWeight: '600'
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
  xs: 2px
  sm: 4px
  md: 8px
  lg: 16px
  row-height: 28px
  input-height: 32px
  gutter: 12px
---

## Brand & Style

This design system is engineered for high-performance data environments where information density and clarity are paramount. The aesthetic rejects decorative trends like glassmorphism or gradients in favor of a "Technical Minimalism" style. It evokes a sense of reliability, precision, and institutional authority, similar to CAD software or industrial control panels.

The UI is optimized for expert users who require maximum data visibility with minimal ocular fatigue. Every element serves a functional purpose, utilizing thin 1px strokes and a restrained palette to create a focused, low-distraction workspace. The emotional response is one of controlled efficiency and professional rigor.

## Colors

The color strategy utilizes a neutral foundation to ensure that semantic signals and primary actions are immediately identifiable. 

- **Foundation:** The background (#F5F5F7) provides a cool, clinical canvas. Text (#111111) is kept near-black for maximum contrast against the light background.
- **Accentuation:** Cobalt Blue (#2C5AA0) is reserved strictly for primary actions, active states, and focus indicators.
- **Status:** Semantic colors are slightly desaturated to remain professional while maintaining high enough contrast for accessibility. Success Green, Warning Amber, and Error Red are used for status pips, data alerts, and destructive actions.
- **Borders:** A consistent light gray (#D1D5DB) is used for all structural containment to define the high-density grid without adding visual bulk.

## Typography

This system employs a dual-font strategy to separate UI narrative from raw data.

1. **Inter:** Used for all UI labels, navigation, and instructional body text. It provides exceptional legibility at small scales (11px-13px).
2. **JetBrains Mono:** Used exclusively for numeric values, code snippets, and table data. The monospaced nature ensures that columns of numbers align vertically, facilitating rapid visual scanning and comparison.

Hierarchy is established through weight and capitalization rather than large shifts in point size to maintain information density. Text is never pure gray; it is either primary black or a subtle 70% opacity for secondary labels.

## Layout & Spacing

The layout philosophy follows a **Fixed Grid** within panels, while the overall dashboard behaves as a **Fluid Grid** to maximize screen real estate on large monitors.

- **Micro-spacing:** A strict 4px baseline grid is used. Padding within components is aggressive: 4px or 8px horizontal padding is standard.
- **Vertical Density:** Table rows are capped at 28px. Form inputs and buttons are capped at 32px. This allows for approximately 30-40% more data visibility compared to standard enterprise software.
- **Structure:** Content is organized into "Panels" separated by 1px borders. Gutters between major modules are kept at 12px to maintain a compact feel while preventing visual bleed.

## Elevation & Depth

In keeping with the engineering aesthetic, this design system avoids shadows to signify depth. Instead, it utilizes **Tonal Layers** and **1px Outlines**.

- **Level 0 (Background):** #F5F5F7.
- **Level 1 (Panels/Cards):** White (#FFFFFF) with a 1px #D1D5DB border.
- **Level 2 (Modals/Popovers):** White (#FFFFFF) with a slightly darker 1px #9CA3AF border. A very subtle, tight 4px blur (opacity 10%) may be used only for global modals to separate them from the primary grid.
- **Active State:** Depth is indicated by "inset" styles for pressed buttons or "active" side-border highlights in cobalt blue.

## Shapes

The shape language is rigid and geometric. 

- **Corners:** A maximum radius of 4px is applied to primary containers and buttons. Smaller components like checkboxes or tags use a 2px radius.
- **Strokes:** A uniform 1px stroke width is applied to all borders. No "heavy" borders or variable stroke widths are permitted, as they degrade the precision of the high-density grid.
- **Interactive Elements:** Buttons and inputs are strictly rectangular with minimal 4px rounding to maintain the "software tool" appearance.

## Components

- **Buttons:** 32px height. Primary buttons use #2C5AA0 with white text. Secondary buttons use a white fill with a #D1D5DB border. Text is 13px Inter, Semi-bold.
- **Input Fields:** 32px height. 1px border. Focus state is indicated by a 1px Cobalt Blue internal ring. No drop shadows.
- **Data Tables:** 28px row height. Headers are 11px Uppercase Inter with a light gray background (#E5E7EB). Cells use 12px JetBrains Mono. 1px horizontal dividers only; vertical dividers are avoided unless necessary for complex multi-variant tables.
- **Chips/Status Pips:** Small 20px height, 2px radius. Backgrounds are tinted at 10% opacity of the semantic color with a 100% opacity text color for high legibility.
- **Iconography:** Use 16px line icons for buttons and 20px for section headers. Stroke weight should be "Regular" to match the 1px UI borders.
- **Tree Navigation:** High-density vertical lists with 24px indentations and 14px chevron icons.