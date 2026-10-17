import os, sys, MigrationHelper

if len(sys.argv) != 5: raise Exception("parameters: path, program_version, feature_version, confirm")

path = sys.argv[1]
program_version = int(sys.argv[2]) #increased for breaking changes
feature_version = int(sys.argv[3]) #increased for new features without breaking changes
confirm = int(sys.argv[4])

mh = MigrationHelper.MigrationHelper(path)
migrations = mh.get_migrations_todo(program_version, feature_version)
if confirm < 1:
    print(len(migrations))
else:
    for m in migrations:
        exec(open(m).read())