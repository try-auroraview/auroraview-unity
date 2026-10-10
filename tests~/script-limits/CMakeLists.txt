cmake_minimum_required(VERSION 3.24)
project(auroraview_script_limits LANGUAGES CXX)

add_executable(script_limits_test main.cpp)
target_compile_features(script_limits_test PRIVATE cxx_std_17)
target_include_directories(script_limits_test PRIVATE "../../native")
if(MSVC)
  target_compile_options(script_limits_test PRIVATE /W4 /WX /EHsc /utf-8)
else()
  target_compile_options(script_limits_test PRIVATE -Wall -Wextra -Werror)
endif()
add_custom_target(check
  COMMAND $<TARGET_FILE:script_limits_test>
  DEPENDS script_limits_test
  VERBATIM)
