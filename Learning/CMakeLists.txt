cmake_minimum_required(VERSION 3.20)
project(AIHealthcareLearning LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_library(ahc_learning
    src/ahc_learning.cpp
)
target_include_directories(ahc_learning PUBLIC include)

if(MSVC)
    target_compile_options(ahc_learning PUBLIC /utf-8)
endif()

add_executable(ahc_learning_cli src/main.cpp)
target_link_libraries(ahc_learning_cli PRIVATE ahc_learning)

enable_testing()
add_executable(ahc_learning_tests tests/ahc_learning_tests.cpp)
target_link_libraries(ahc_learning_tests PRIVATE ahc_learning)
add_test(NAME ahc_learning_tests COMMAND ahc_learning_tests)
