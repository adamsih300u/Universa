FROM node:20-alpine

WORKDIR /app

# Install dependencies when building the image
COPY package*.json ./
RUN npm install

# The source code will be mounted as a volume
CMD ["npm", "run", "dev"]

EXPOSE 5173

# Development mode
CMD ["npm", "run", "dev", "--", "--host"] 