import { Burger, Group } from "@mantine/core";

type TopbarProps = {
    sidebarOpened: boolean;
    onToggleSidebar: () => void;
};

function Topbar({sidebarOpened, onToggleSidebar}: TopbarProps) {
    return (
        <Group
            h="100%"
            px="md"
            justify="space-between"
        >
            <Group>
                <Burger
                    opened={sidebarOpened}
                    onClick={onToggleSidebar}
                    size="sm"
                    aria-label="Toggle sidebar"
                />
                <span>Analytic Dashboard</span>
            </Group>

            <Group>
                <span>Jan Rojek</span>
                <img src="https://placehold.co/32x32" alt="Avatar" />
                <button>▾</button>
            </Group>
        </Group>
    );
}

export default Topbar;
