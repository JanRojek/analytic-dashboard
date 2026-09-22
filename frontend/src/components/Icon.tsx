import type { CSSProperties } from "react";

const paths = {
  plus: "M12 5v14M5 12h14",
  arrow: "M4 12h15M13 6l6 6-6 6",
  back: "M20 12H5m6-6-6 6 6 6",
  chevron: "m9 5 7 7-7 7",
  down: "m6 9 6 6 6-6",
  search: "M21 21l-5-5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0",
  grid: "M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z",
  folder:
    "M3 7V5a1 1 0 0 1 1-1h5l2 3h9a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z",
  data: "M4 4h16v16H4zM4 9h16M4 14h16M9 4v16",
  chart: "M4 3v17h17M8 15V9m5 6V5m5 10v-4",
  line: "M3 3v18h18M6 15l4-5 4 2 6-7",
  pie: "M12 3v9h9M9 3.5a9 9 0 1 0 11.5 11.5",
  dashboard: "M3 3h18v18H3zM3 9h18M11 9v12",
  upload: "M12 16V3m-5 5 5-5 5 5M4 15v5h16v-5",
  download: "M12 3v13m-5-5 5 5 5-5M4 16v5h16v-5",
  check: "m5 12 4 4L19 6",
  close: "m6 6 12 12M6 18 18 6",
  more: "M5 12h.01M12 12h.01M19 12h.01",
  clock: "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0M12 7v5l3 2",
  star: "m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9z",
  settings: "M4 7h16M4 17h16M8 4v6m8 4v6",
  help: "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0M9 9a3 3 0 0 1 6 0c0 2-3 2-3 4m0 3h.01",
  logout: "M9 4H4v16h5m5-13 5 5-5 5M8 12h11",
  book: "M12 5c-3-2-6-2-9-1v15c3-1 6-1 9 1 3-2 6-2 9-1V4c-3-1-6-1-9 1v15",
  bolt: "m13 2-9 12h7l-1 8 10-13h-7z",
  trash: "M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7m4-7v7",
  edit: "m16 3 5 5L9 20l-6 1 1-6zM13 6l5 5",
  filter: "M3 4h18l-7 8v7l-4 2v-9z",
  shield: "m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6zM8 12l3 3 5-6",
  mail: "M3 5h18v14H3zM3 5l9 7 9-7",
  lock: "M5 10h14v11H5zM8 10V6a4 4 0 0 1 8 0v4",
  play: "m7 4 14 8-14 8z",
  undo: "M3 4v6h6M3 10c3-7 17-7 17 3a7 7 0 0 1-10 6",
  warning: "m12 3 10 18H2zM12 9v5m0 3h.01",
  menu: "M4 6h16M4 12h16M4 18h16",
  spark: "m12 2 3 7 7 3-7 3-3 7-3-7-7-3 7-3z",
  list: "M8 5h13M8 12h13M8 19h13M3 5h.01M3 12h.01M3 19h.01",
  eye: "M2 12s4-7 10-7 10 7 10 7-4 7-10 7-10-7-10-7M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0",
  grip: "M9 5h.01M15 5h.01M9 12h.01M15 12h.01M9 19h.01M15 19h.01",
  number: "M10 3 8 21M16 3l-2 18M4 9h16M3 15h16",
  text: "M4 5h16M12 5v15M8 20h8",
  calendar: "M4 5h16v16H4zM4 10h16M8 3v4m8-4v4",
  copy: "M8 8h13v13H8zM16 8V3H3v13h5",
  globe:
    "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0M3 12h18M12 3c5 5 5 13 0 18-5-5-5-13 0-18",
} as const;

export type IconName = keyof typeof paths;
export function Icon({
  name,
  size = 18,
  style,
  className,
}: {
  name: IconName;
  size?: number;
  style?: CSSProperties;
  className?: string;
}) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.65}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      style={style}
      className={className}
    >
      <path d={paths[name]} />
    </svg>
  );
}
