# DISM GUI — direct GitHub reporting setup

Status: 4.4.0 development only. The desktop entry point and live authorization are not yet verified.

The repository owner must register a GitHub App, grant only Issues: Read and write, enable Device Flow, and install the app on sgennadi/DISM-GUI. The app's public Client ID can be shared with users; never share a Client Secret or private key.

Each user must sign in to GitHub and approve their own connection. GitHub does not offer anonymous public Issue creation via REST. No report may be transmitted without explicit consent.

GitHub Docs: https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app
