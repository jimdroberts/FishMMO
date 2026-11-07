# Applies a patch to the current (git) source directory unless it is already applied.
# Used as a FetchContent PATCH_COMMAND, which can run again on an existing checkout.
#   cmake -DPATCH_FILE=<file.patch> -P ApplyPatchOnce.cmake
if(NOT PATCH_FILE)
    message(FATAL_ERROR "PATCH_FILE is not set")
endif()
execute_process(
    COMMAND git apply --reverse --check "${PATCH_FILE}"
    RESULT_VARIABLE already_applied
    OUTPUT_QUIET ERROR_QUIET)
if(already_applied EQUAL 0)
    message(STATUS "Patch already applied: ${PATCH_FILE}")
    return()
endif()
execute_process(
    COMMAND git apply --whitespace=nowarn "${PATCH_FILE}"
    RESULT_VARIABLE apply_failed)
if(NOT apply_failed EQUAL 0)
    message(FATAL_ERROR "Could not apply ${PATCH_FILE}; the fetched source no longer matches it")
endif()
message(STATUS "Applied patch: ${PATCH_FILE}")
