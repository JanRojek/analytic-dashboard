import {
  ActionIcon,
  Badge,
  Button,
  createTheme,
  Drawer,
  Menu,
  Modal,
  Popover,
  SegmentedControl,
  Select,
  Table,
  Tabs,
  Tooltip,
  type MantineColorsTuple,
} from '@mantine/core'

// Warm neutral scale shared by surfaces, borders and text.
const stone: MantineColorsTuple = [
  '#fafaf9',
  '#f5f5f4',
  '#e7e5e4',
  '#d6d3d1',
  '#a8a29e',
  '#78716c',
  '#57534e',
  '#44403c',
  '#292524',
  '#1c1917',
]

// "Ink" drives primary actions: dark, confident buttons instead of a colored fill.
const ink: MantineColorsTuple = [
  '#f5f5f4',
  '#e7e5e4',
  '#d6d3d1',
  '#a8a29e',
  '#78716c',
  '#57534e',
  '#44403c',
  '#292524',
  '#1c1917',
  '#0c0a09',
]

// Restrained teal accent for selection, focus, links and progress.
const accent: MantineColorsTuple = [
  '#f0fdfa',
  '#ccfbf1',
  '#99f6e4',
  '#5eead4',
  '#2dd4bf',
  '#14b8a6',
  '#0d9488',
  '#0f766e',
  '#115e59',
  '#134e4a',
]

const fontFamily =
  "'Inter Variable', ui-sans-serif, system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"

export const theme = createTheme({
  fontFamily,
  fontFamilyMonospace:
    "ui-monospace, 'JetBrains Mono', SFMono-Regular, Menlo, Consolas, monospace",
  primaryColor: 'accent',
  primaryShade: 7,
  colors: { gray: stone, ink, accent, dark: stone },
  black: '#1c1917',
  white: '#ffffff',
  defaultRadius: 'md',
  radius: { xs: '4px', sm: '6px', md: '8px', lg: '12px', xl: '16px' },
  fontSizes: { xs: '12px', sm: '13px', md: '14px', lg: '16px', xl: '18px' },
  lineHeights: { xs: '1.4', sm: '1.45', md: '1.5', lg: '1.5', xl: '1.5' },
  headings: {
    fontFamily,
    fontWeight: '600',
    sizes: {
      h1: { fontSize: '24px', lineHeight: '1.25', fontWeight: '600' },
      h2: { fontSize: '20px', lineHeight: '1.3', fontWeight: '600' },
      h3: { fontSize: '16px', lineHeight: '1.35', fontWeight: '600' },
      h4: { fontSize: '14px', lineHeight: '1.4', fontWeight: '600' },
      h5: { fontSize: '13px', lineHeight: '1.4', fontWeight: '600' },
      h6: { fontSize: '12px', lineHeight: '1.4', fontWeight: '600' },
    },
  },
  shadows: {
    xs: '0 1px 2px rgba(28, 25, 23, 0.06)',
    sm: '0 1px 3px rgba(28, 25, 23, 0.08), 0 1px 2px rgba(28, 25, 23, 0.04)',
    md: '0 4px 12px -2px rgba(28, 25, 23, 0.10), 0 2px 4px rgba(28, 25, 23, 0.05)',
    lg: '0 12px 32px -8px rgba(28, 25, 23, 0.16), 0 4px 8px rgba(28, 25, 23, 0.06)',
    xl: '0 24px 56px -12px rgba(28, 25, 23, 0.22), 0 8px 16px rgba(28, 25, 23, 0.06)',
  },
  cursorType: 'pointer',
  focusRing: 'auto',
  respectReducedMotion: true,
  autoContrast: true,
  components: {
    Button: Button.extend({
      defaultProps: { color: 'ink', size: 'sm' },
    }),
    ActionIcon: ActionIcon.extend({
      defaultProps: { variant: 'subtle', color: 'gray', size: 'md' },
    }),
    Badge: Badge.extend({
      defaultProps: { variant: 'light', radius: 'sm', size: 'sm', tt: 'none', fw: 500 },
    }),
    Modal: Modal.extend({
      defaultProps: {
        centered: true,
        radius: 'lg',
        padding: 'xl',
        overlayProps: { color: '#1c1917', backgroundOpacity: 0.4 },
        transitionProps: { transition: 'pop', duration: 160 },
      },
    }),
    Drawer: Drawer.extend({
      defaultProps: {
        overlayProps: { color: '#1c1917', backgroundOpacity: 0.3 },
        padding: 'lg',
      },
    }),
    Menu: Menu.extend({
      defaultProps: { shadow: 'lg', radius: 'md', offset: 6, withinPortal: true },
    }),
    Popover: Popover.extend({
      defaultProps: { shadow: 'lg', radius: 'md' },
    }),
    Tooltip: Tooltip.extend({
      defaultProps: {
        color: 'ink',
        radius: 'sm',
        openDelay: 350,
        transitionProps: { duration: 120 },
        withinPortal: true,
      },
    }),
    Select: Select.extend({
      defaultProps: { checkIconPosition: 'right', allowDeselect: false },
    }),
    Tabs: Tabs.extend({
      defaultProps: { color: 'accent' },
    }),
    SegmentedControl: SegmentedControl.extend({
      defaultProps: { radius: 'md', size: 'sm' },
    }),
    Table: Table.extend({
      defaultProps: { verticalSpacing: 'sm', horizontalSpacing: 'md' },
    }),
  },
})
