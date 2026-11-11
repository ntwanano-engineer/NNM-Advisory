# NNM Advisory Frontend — development image (Vite dev server)
FROM node:20-alpine AS dev
WORKDIR /app

# Copy manifests and install dependencies before copying source
# so package layers are cached independently of source changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . ./

# Vite dev server listens on port 3000 inside the container.
EXPOSE 3000

# --host 0.0.0.0 makes Vite reachable from outside the container.
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0", "--port", "3000"]
