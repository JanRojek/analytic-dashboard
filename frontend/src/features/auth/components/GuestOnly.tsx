import type { ReactNode } from "react";
import { Navigate } from "react-router-dom";

import { useSession } from "../../../session/SessionContext";

type GuestOnlyProps = {
    children: ReactNode;
};

export function GuestOnly({ children }: GuestOnlyProps) {
    const { session } = useSession();

    if (session) {
        return <Navigate to="/projects" replace />;
    }

    return children;
}
