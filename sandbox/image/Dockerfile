FROM docker/sandbox-templates:claude-code-docker

LABEL org.opencontainers.image.source="https://github.com/markeli/ai-agents-tooling" \
	org.opencontainers.image.description="Claude Code sandbox template for the markeli-agent bot"

# Several RUN steps below pipe curl into jq/sha256sum; pipefail makes an upstream failure fail the whole step
SHELL ["/bin/bash", "-o", "pipefail", "-c"]

ARG GLAB_VERSION=1.119.0
ARG DOTNET_CHANNELS="8.0 9.0 10.0"
ARG NODE_MAJOR=24
ARG YQ_VERSION=4.54.1
ARG ACTIONLINT_VERSION=1.7.12
ARG HADOLINT_VERSION=2.15.1
ARG TERRAFORM_VERSION=1.16.5
ARG BOT_NAME=markeli-agent
ARG BOT_EMAIL=markelow.dev+agent@gmail.com

# "root" is the meaningful name here, not a numeric id
# hadolint ignore=DL3066
USER root

# Base image ships Ubuntu's distro Node 22; swap in the NodeSource LTS build so node/npm track upstream directly.
# NodeSource publishes frequent patch releases; NODE_MAJOR above already pins the LTS line
# hadolint ignore=DL3008
RUN curl -fsSL "https://deb.nodesource.com/setup_${NODE_MAJOR}.x" -o /tmp/setup-node.sh \
	&& bash /tmp/setup-node.sh \
	&& apt-get install -y --no-install-recommends nodejs \
	&& rm -f /tmp/setup-node.sh \
	&& rm -rf /var/lib/apt/lists/*
# Suppresses corepack's interactive "download pnpm/yarn?" prompt; the agent user pre-activates both below
ENV COREPACK_ENABLE_DOWNLOAD_PROMPT=0
RUN corepack enable

# Versions track whatever Ubuntu 26.04 ships; pinning would break on base image bumps
# hadolint ignore=DL3008
RUN export DEBIAN_FRONTEND=noninteractive \
	&& apt-get update \
	&& apt-get install -y --no-install-recommends \
		build-essential \
		shellcheck \
		shfmt \
		fd-find \
		tree \
		zip \
		sqlite3 \
		postgresql-client \
	&& ln -s /usr/bin/fdfind /usr/local/bin/fd \
	&& rm -rf /var/lib/apt/lists/*

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

# yq: GitHub publishes a sha256 asset digest via the API, which sidesteps yq's own multi-hash checksums file format
RUN arch=$(dpkg --print-architecture) \
	&& asset="yq_linux_${arch}" \
	&& digest=$(curl -fsSL "https://api.github.com/repos/mikefarah/yq/releases/tags/v${YQ_VERSION}" \
		| jq -r --arg name "$asset" '.assets[] | select(.name == $name) | .digest' | sed 's/^sha256://') \
	&& [ -n "$digest" ] \
	&& curl -fsSL "https://github.com/mikefarah/yq/releases/download/v${YQ_VERSION}/${asset}" -o /usr/local/bin/yq \
	&& echo "${digest}  /usr/local/bin/yq" | sha256sum -c - \
	&& chmod +x /usr/local/bin/yq

RUN arch=$(dpkg --print-architecture) \
	&& asset="actionlint_${ACTIONLINT_VERSION}_linux_${arch}.tar.gz" \
	&& digest=$(curl -fsSL "https://api.github.com/repos/rhysd/actionlint/releases/tags/v${ACTIONLINT_VERSION}" \
		| jq -r --arg name "$asset" '.assets[] | select(.name == $name) | .digest' | sed 's/^sha256://') \
	&& [ -n "$digest" ] \
	&& curl -fsSL "https://github.com/rhysd/actionlint/releases/download/v${ACTIONLINT_VERSION}/${asset}" -o /tmp/actionlint.tar.gz \
	&& echo "${digest}  /tmp/actionlint.tar.gz" | sha256sum -c - \
	&& tar -xzf /tmp/actionlint.tar.gz -C /usr/local/bin actionlint \
	&& rm /tmp/actionlint.tar.gz

# hadolint's release assets use the x86_64/arm64 arch names, unlike Debian's amd64/arm64
RUN arch=$(dpkg --print-architecture) \
	&& case "$arch" in \
		amd64) hadolint_arch=x86_64 ;; \
		arm64) hadolint_arch=arm64 ;; \
		*) echo "unsupported architecture: $arch" >&2; exit 1 ;; \
	esac \
	&& asset="hadolint-linux-${hadolint_arch}" \
	&& digest=$(curl -fsSL "https://api.github.com/repos/hadolint/hadolint/releases/tags/v${HADOLINT_VERSION}" \
		| jq -r --arg name "$asset" '.assets[] | select(.name == $name) | .digest' | sed 's/^sha256://') \
	&& [ -n "$digest" ] \
	&& curl -fsSL "https://github.com/hadolint/hadolint/releases/download/v${HADOLINT_VERSION}/${asset}" -o /usr/local/bin/hadolint \
	&& echo "${digest}  /usr/local/bin/hadolint" | sha256sum -c - \
	&& chmod +x /usr/local/bin/hadolint

# A subshell cd keeps this scoped to one command instead of changing WORKDIR for the rest of the file
# hadolint ignore=DL3003
RUN arch=$(dpkg --print-architecture) \
	&& asset="terraform_${TERRAFORM_VERSION}_linux_${arch}.zip" \
	&& curl -fsSL "https://releases.hashicorp.com/terraform/${TERRAFORM_VERSION}/${asset}" -o "/tmp/${asset}" \
	&& curl -fsSL "https://releases.hashicorp.com/terraform/${TERRAFORM_VERSION}/terraform_${TERRAFORM_VERSION}_SHA256SUMS" -o /tmp/terraform_SHA256SUMS \
	&& (cd /tmp && grep " ${asset}\$" terraform_SHA256SUMS | sha256sum -c -) \
	&& unzip -q -o "/tmp/${asset}" -d /usr/local/bin terraform \
	&& rm "/tmp/${asset}" /tmp/terraform_SHA256SUMS

# sbx copies the host user's git identity into ~/.gitconfig, so env vars pin the bot identity above any config file
ENV GIT_AUTHOR_NAME=${BOT_NAME} \
	GIT_AUTHOR_EMAIL=${BOT_EMAIL} \
	GIT_COMMITTER_NAME=${BOT_NAME} \
	GIT_COMMITTER_EMAIL=${BOT_EMAIL}

# Fetch over HTTPS stays anonymous (public repos, plugin marketplaces); pushes go over SSH with the forwarded bot key
RUN git config --system url."git@github.com:".pushInsteadOf "https://github.com/" \
	&& git config --system url."git@gitlab.com:".pushInsteadOf "https://gitlab.com/" \
	&& ssh-keyscan -t ed25519 github.com gitlab.com >> /etc/ssh/ssh_known_hosts

# Adds both co-author trailers to every commit, however it is made; each other hook chains to the repository's own
COPY git-hooks/ /etc/git-hooks/
RUN git config --system core.hooksPath /etc/git-hooks

# sbx regenerates ~/.claude/settings.json on every create; managed config under /etc/claude-code survives
COPY managed-settings.json /etc/claude-code/managed-settings.json
COPY CLAUDE.md /etc/claude-code/CLAUDE.md

# "agent" is the base image's fixed non-root account, not a numeric id
# hadolint ignore=DL3066
USER agent

RUN dotnet tool install --global csharp-ls
ENV PATH="/home/agent/.dotnet/tools:${PATH}"

# Pre-fetches pnpm/yarn into the agent's corepack cache; pnpm --version also triggers its one-time native binary
# download, so neither happens on the agent's first real use
RUN corepack prepare pnpm@latest --activate \
	&& corepack prepare yarn@stable --activate \
	&& pnpm --version >/dev/null \
	&& yarn --version >/dev/null

RUN uv tool install ansible-core \
	&& uv tool install ansible-lint

RUN claude plugin marketplace add anthropics/claude-plugins-official \
	&& claude plugin install superpowers@claude-plugins-official \
	&& claude plugin install csharp-lsp@claude-plugins-official
