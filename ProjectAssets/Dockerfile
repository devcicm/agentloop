FROM node:24-bookworm
# Host-owned toolchain. Generated projects cannot change this image or the oracle mount.
RUN npm install --global typescript@5.7.3 playwright@1.56.1 && \
    PLAYWRIGHT_BROWSERS_PATH=/opt/browsers /usr/local/lib/node_modules/playwright/cli.js install --with-deps chromium && \
    chmod -R a+rX /opt/browsers
ENV PLAYWRIGHT_BROWSERS_PATH=/opt/browsers
USER node
WORKDIR /tmp
