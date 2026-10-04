IMAGE ?= ghcr.io/markeli/claude-sandbox
TAG ?= latest
PLATFORM ?= linux/arm64
KIT := sandbox/kits/markeli-claude

.PHONY: image load validate

## image: build the sandbox template image locally
image:
	docker build --platform $(PLATFORM) -t $(IMAGE):$(TAG) sandbox/image

## load: build the image and load it into the sbx runtime (sbx does not share the host Docker image store)
load: image
	tmp=$$(mktemp -d) && trap 'rm -rf "$$tmp"' EXIT \
		&& docker image save $(IMAGE):$(TAG) -o "$$tmp/image.tar" \
		&& sbx template load "$$tmp/image.tar"

## validate: check the kit descriptor
validate:
	sbx kit validate $(KIT)
