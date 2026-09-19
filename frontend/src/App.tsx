import {
  Navigate,
  Route,
  Routes,
} from "react-router-dom";
import AppLayout from "./components/AppLayout";
import ProjectsPage from "./pages/ProjectsPage.tsx";
import LoginPage from "./pages/LoginPage";

function HomePage() {
  return <div>Home</div>;
}

export default function App() {
  return (
      <Routes>
        <Route path="/" element={<HomePage />} />

        <Route path="/login" element={<LoginPage />} />

        <Route element={<AppLayout />}>
            <Route path="projects" element={<ProjectsPage />} />
        </Route>

        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
  );
}
