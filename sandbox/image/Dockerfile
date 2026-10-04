FROM docker/sandbox-templates:claude-code-docker

LABEL org.opencontainers.image.source="https://github.com/markeli/ai-agents-tooling" \
	org.opencontainers.image.description="Claude Code sandbox template for the markeli-agent bot"

ARG GLAB_VERSION=1.119.0
ARG DOTNET_CHANNELS="8.0 9.0 10.0"
ARG BOT_NAME=markeli-agent
ARG BOT_EMAIL=markelow.dev+agent@gmail.com

USER root

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
	&& for channel in ${DOTNET_CHANNELS}; do bash /tmp/dotnet-install.sh --channel "$channel" --install-dir /usr/share/dotnet; done \
	&& ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
	&& rm /tmp/dotnet-install.sh
ENV DOTNET_ROOT=/usr/share/dotnet \
	DOTNET_CLI_TELEMETRY_OPTOUT=1 \
	DOTNET_NOLOGO=1

RUN arch=$(dpkg --print-architecture) \
	&& curl -fsSL "https://gitlab.com/gitlab-org/cli/-/releases/v${GLAB_VERSION}/downloads/glab_${GLAB_VERSION}_linux_${arch}.deb" -o /tmp/glab.deb \
	&& dpkg -i /tmp/glab.deb \
	&& rm /tmp/glab.deb

# sbx copies the host user's git identity into ~/.gitconfig, so env vars pin the bot identity above any config file
ENV GIT_AUTHOR_NAME=${BOT_NAME} \
	GIT_AUTHOR_EMAIL=${BOT_EMAIL} \
	GIT_COMMITTER_NAME=${BOT_NAME} \
	GIT_COMMITTER_EMAIL=${BOT_EMAIL}

# Fetch over HTTPS stays anonymous (public repos, plugin marketplaces); pushes go over SSH with the forwarded bot key
RUN git config --system url."git@github.com:".pushInsteadOf "https://github.com/" \
	&& git config --system url."git@gitlab.com:".pushInsteadOf "https://gitlab.com/" \
	&& ssh-keyscan -t ed25519 github.com gitlab.com >> /etc/ssh/ssh_known_hosts

# sbx regenerates ~/.claude/settings.json on every create; managed config under /etc/claude-code survives
COPY managed-settings.json /etc/claude-code/managed-settings.json
COPY CLAUDE.md /etc/claude-code/CLAUDE.md

USER agent

RUN dotnet tool install --global csharp-ls
ENV PATH="/home/agent/.dotnet/tools:${PATH}"

RUN claude plugin marketplace add anthropics/claude-plugins-official \
	&& claude plugin install superpowers@claude-plugins-official \
	&& claude plugin install csharp-lsp@claude-plugins-official
