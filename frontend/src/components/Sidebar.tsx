import { NavLink, Stack } from "@mantine/core";

function Sidebar() {
    return (
        <Stack gap="xs" p="sm">
            <NavLink
                label="Projects"
                active
            />
        </Stack>
    );
}

export default Sidebar;
