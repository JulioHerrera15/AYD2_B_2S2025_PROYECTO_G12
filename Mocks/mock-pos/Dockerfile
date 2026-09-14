FROM node:20-alpine

WORKDIR /app

COPY package*.json ./
RUN npm install --omit=dev

COPY server.js ./

ENV PORT=4001
EXPOSE 4001

CMD ["node", "server.js"]
