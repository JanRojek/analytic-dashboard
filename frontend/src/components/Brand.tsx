import { Link } from "react-router-dom";

// Temporary product identity lives here and in the theme tokens.
export function Brand({ light = false }: { light?: boolean }) {
  return (
    <Link
      to="/projects"
      className={`brand ${light ? "brand-light" : ""}`}
      aria-label="Aperture home"
    >
      <svg
        width="30"
        height="30"
        viewBox="0 0 32 32"
        fill="none"
        aria-hidden="true"
      >
        <path
          d="M4 4h10v6H10v4H4V4Zm14 0h10v10h-6v-4h-4V4ZM4 18h6v4h4v6H4V18Zm18 0h6v10H18v-6h4v-4Z"
          fill="currentColor"
        />
      </svg>
      <span>
        aperture<span className="brand-period">.</span>
      </span>
    </Link>
  );
}
