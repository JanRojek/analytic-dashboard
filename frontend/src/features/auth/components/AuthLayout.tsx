import type { ReactNode } from "react";
import { Brand } from "../../../components/Brand";
import { Icon } from "../../../components/Icon";
import "../styles/auth-layout.css";
import "../styles/auth-artwork.css";
import "../styles/auth-form.css";

function AuthArtwork() {
    return (
        <div className="auth-art" aria-hidden="true">
            <div className="orbit orbit-one" />
            <div className="orbit orbit-two" />
            <div className="art-paper art-paper-back" />
            <div className="art-paper">
                <div className="art-label">
                    <span className="tiny-dot" /> A NEW PERSPECTIVE
                </div>
                <div className="art-number">Ideas into insight.</div>
                <div className="art-chart">
                    <svg viewBox="0 0 350 135" fill="none">
                        <path
                            d="M0 35H350M0 75H350M0 115H350"
                            stroke="#e2e8df"
                            strokeDasharray="3 5"
                        />
                        <path
                            d="M0 110 35 98 70 106 105 78 140 87 175 55 210 61 245 25 280 38 315 12 350 4V135H0Z"
                            fill="#dfebdc"
                        />
                        <path
                            d="M0 110 35 98 70 106 105 78 140 87 175 55 210 61 245 25 280 38 315 12 350 4"
                            stroke="#427555"
                            strokeWidth="2.5"
                        />
                    </svg>
                </div>
                <div className="art-axis">
                    <span>YOUR DATA</span>
                    <span>YOUR NEXT CHAPTER</span>
                </div>
            </div>
            <div className="art-floating">
        <span className="art-check">
          <Icon name="check" size={16} />
        </span>
                <div>
                    <strong>Make the connection</strong>
                    <span>From the first row to the bigger picture.</span>
                </div>
            </div>
        </div>
    );
}

type AuthLayoutProps = {
    children: ReactNode;
    topAction: ReactNode;
};

export function AuthLayout({
    children,
    topAction,
}: AuthLayoutProps) {
    return (
        <div className="auth-layout">
            <aside className="auth-story">
                <Brand />

                <div className="auth-story-body">
                    <div className="eyebrow">
                        A LITTLE CLARITY GOES A LONG WAY
                    </div>

                    <div className="auth-story-title">
                        Your data.
                        <br />
                        A clearer perspective.
                    </div>

                    <p>
                        Bring the pieces together. Find the story in your data, and make it
                        something worth sharing.
                    </p>

                    <AuthArtwork />
                </div>

                <div className="auth-story-footer">
                    <span>Collect. Understand. Create.</span>
                    <span>Made for the curious.</span>
                </div>
            </aside>

            <main className="auth-main">
                <div className="auth-top">
                    {topAction}
                </div>

                <div className="auth-form-wrap">
                    <div className="mobile-brand">
                        <Brand />
                    </div>

                    {children}
                </div>
            </main>
        </div>
    );
}
