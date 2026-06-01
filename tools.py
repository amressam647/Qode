
import os
import subprocess

class AgentTools:
    def __init__(self, workspace_path):
        self.workspace_path = workspace_path

    def list_dir(self, rel_path="."):
        path = os.path.join(self.workspace_path, rel_path)
        try:
            return str(os.listdir(path))
        except Exception as e:
            return f"Error: {e}"

    def read_file(self, rel_path):
        path = os.path.join(self.workspace_path, rel_path)
        try:
            with open(path, "r", encoding="utf-8") as f:
                return f.read()
        except Exception as e:
            return f"Error reading file: {e}"

    def write_file(self, rel_path, content):
        path = os.path.join(self.workspace_path, rel_path)
        try:
            with open(path, "w", encoding="utf-8") as f:
                f.write(content)
            return f"Successfully wrote to {rel_path}"
        except Exception as e:
            return f"Error writing file: {e}"

    def run_command(self, command):
        try:
            result = subprocess.run(
                command, 
                shell=True, 
                cwd=self.workspace_path, 
                capture_output=True, 
                text=True
            )
            return f"STDOUT:\n{result.stdout}\nSTDERR:\n{result.stderr}"
        except Exception as e:
            return f"Error executing command: {e}"
