#!/bin/bash
set -e

for database in catalogdb inventorydb ordersdb paymentsdb keycloakdb
do
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-EOSQL
    SELECT 'CREATE DATABASE ${database}'
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '${database}')\gexec
EOSQL
done
