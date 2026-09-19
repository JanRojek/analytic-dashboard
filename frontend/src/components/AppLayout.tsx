import { useState } from "react";
import { AppShell } from "@mantine/core";
import { Outlet } from "react-router-dom";
import Topbar from "./Topbar";
import Sidebar from "./Sidebar";
import classes from "./AppLayout.module.css";

function AppLayout() {
    const [sidebarOpened, setSidebarOpened] = useState(true);

    return (
        <AppShell
            className={classes.shell}
            header={{ height: 60 }}
            navbar={{
                width: 240,
                breakpoint: "sm",
                collapsed: {
                    desktop: !sidebarOpened,
                    mobile: !sidebarOpened,
                },
            }}
            padding="md"
        >
            <AppShell.Header>
                <Topbar
                    sidebarOpened={sidebarOpened}
                    onToggleSidebar={() =>
                        setSidebarOpened((opened) => !opened)
                    }
                />
            </AppShell.Header>

            <AppShell.Navbar>
                <Sidebar />
            </AppShell.Navbar>

            <AppShell.Main>
                <Outlet />
            </AppShell.Main>
        </AppShell>
    );
}

export default AppLayout;
