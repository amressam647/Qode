
import os

class StateManager:
    def __init__(self, workspace_path):
        self.workspace_path = workspace_path
        self.plan_file = os.path.join(workspace_path, "project_plan.md")
        self.tasks_file = os.path.join(workspace_path, "current_tasks.md")
        self._init_files()

    def _init_files(self):
        if not os.path.exists(self.plan_file):
            with open(self.plan_file, "w") as f:
                f.write("# Project Plan\n\n- [ ] Initial Plan")
        if not os.path.exists(self.tasks_file):
            with open(self.tasks_file, "w") as f:
                f.write("# Current Tasks\n\n- [ ] Start working")

    def read_plan(self):
        with open(self.plan_file, "r") as f:
            return f.read()

    def read_tasks(self):
        with open(self.tasks_file, "r") as f:
            return f.read()

    def update_plan(self, content):
        with open(self.plan_file, "w") as f:
            f.write(content)

    def update_tasks(self, content):
        with open(self.tasks_file, "w") as f:
            f.write(content)
