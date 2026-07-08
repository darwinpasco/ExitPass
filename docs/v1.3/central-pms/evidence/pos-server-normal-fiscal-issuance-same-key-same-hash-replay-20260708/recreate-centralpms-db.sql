DO $$
DECLARE
    target_database text := 'centralpms_feq_retry_uat_local';
BEGIN
    IF target_database <> 'centralpms_feq_retry_uat_local'
       OR target_database !~* 'centralpms'
       OR target_database !~* 'feq'
       OR target_database !~* 'retry'
       OR target_database !~* 'uat'
       OR target_database !~* 'local'
       OR target_database ~* '(prod|production|shared|live|exitpass_v12_dev)'
    THEN
        RAISE EXCEPTION 'Refusing to recreate unsafe database: %', target_database;
    END IF;
END $$;
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE datname = 'centralpms_feq_retry_uat_local'
  AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS centralpms_feq_retry_uat_local;
CREATE DATABASE centralpms_feq_retry_uat_local OWNER exitpass;
